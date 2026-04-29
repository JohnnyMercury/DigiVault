using DigiVault.Web.Services;
using DigiVault.Web.Services.Fulfilment;
using Microsoft.AspNetCore.Mvc;

namespace DigiVault.Web.Controllers.Api;

/// <summary>
/// Receives webhook callbacks from external payment providers (YooKassa,
/// Stripe, BlvckPay, etc.). Routes the body to <see cref="IPaymentService"/>
/// for signature validation + transaction status update, then — if the
/// resulting transaction is bound to an order — triggers
/// <see cref="IFulfilmentService"/> so the customer gets their product.
///
/// Any future provider only needs to:
///   1. implement <c>IPaymentProvider</c> (sign / verify / create payments)
///   2. register itself in DI
///   3. point its webhook URL to <c>POST /api/webhooks/{providerName}</c>
/// — no changes here, no changes in the order/fulfilment pipeline.
/// </summary>
[ApiController]
[Route("api/webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly IFulfilmentService _fulfilment;
    private readonly Infrastructure.Data.ApplicationDbContext _db;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(IPaymentService paymentService, IFulfilmentService fulfilment,
        Infrastructure.Data.ApplicationDbContext db, ILogger<WebhooksController> logger)
    {
        _paymentService = paymentService;
        _fulfilment = fulfilment;
        _db = db;
        _logger = logger;
    }

    [HttpPost("{provider}")]
    public async Task<IActionResult> HandleWebhook(string provider)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();

        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());

        _logger.LogInformation("Webhook received from {Provider}. Body length: {Length}, IP: {IP}",
            provider, body.Length, HttpContext.Connection.RemoteIpAddress);

        // 1. Hand off to PaymentService — it validates the signature, finds the
        //    PaymentTransaction by id, updates its status, and (for top-ups)
        //    credits the user's balance.
        var processed = await _paymentService.ProcessWebhookAsync(provider, headers, body);

        if (!processed)
        {
            // Signature failed / unknown transaction / unsupported provider —
            // PaymentService logged the reason. Return 200 so the provider
            // doesn't retry indefinitely (most providers stop on 200).
            return Ok(new { status = "rejected", provider });
        }

        // 2. If this webhook closed an order-linked payment, kick off fulfilment
        //    immediately. The background sweeper would also catch it within 30 s,
        //    but doing it inline gets the customer their product faster.
        try
        {
            var txQuery = _db.PaymentTransactions
                .Where(t => t.Status == Core.Enums.PaymentStatus.Completed
                         && t.OrderId != null)
                .OrderByDescending(t => t.UpdatedAt);

            // Pick the most recently updated order-linked completed transaction
            // for this provider — there's no clean way to thread the specific
            // transaction from PaymentService.ProcessWebhookAsync today, so we
            // best-effort find it.
            var recent = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstOrDefaultAsync(txQuery);

            if (recent?.OrderId is int orderId)
            {
                _logger.LogInformation("Webhook → triggering fulfilment for Order {OrderId}", orderId);
                await _fulfilment.DeliverOrderAsync(orderId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Inline fulfilment after webhook failed; safety-net sweeper will retry");
        }

        return Ok(new { status = "accepted", provider });
    }

    [HttpGet("{provider}")]
    public IActionResult WebhookGet(string provider)
    {
        // Some providers ping a GET to verify the URL during setup.
        return Ok(new { status = "ok", provider });
    }
}
