using System.Text.Json;
using DigiVault.Infrastructure.Data;
using DigiVault.Web.Services.Payment.Providers.PaymentLink;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Controllers;

/// <summary>
/// Bridge endpoints between our flow and PSPs that need a POST-form redirect
/// (PaymentLink in particular). The PaymentLink merchant interface expects
/// the customer to be POST-redirected to start.paymentlnk.com with all
/// parameters in the form body — but our PaymentResult.RedirectUrl is just a
/// GET-able string. This controller bridges the gap: it loads the prepared
/// form from PaymentTransaction.ProviderData and renders an auto-submitting
/// HTML form to the PSP.
/// </summary>
public class PaymentController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<PaymentController> _log;

    public PaymentController(ApplicationDbContext db, ILogger<PaymentController> log)
    {
        _db = db;
        _log = log;
    }

    /// <summary>
    /// GET /Payment/PaymentLinkStart/{transactionId}
    ///
    /// Renders an HTML page that auto-POSTs the prepared params to
    /// PaymentLink. The prepared params (incl. signature) live in
    /// <see cref="DigiVault.Core.Entities.PaymentTransaction.ProviderData"/>
    /// as JSON and were written by <see cref="PaymentLinkPaymentProvider"/>
    /// during CreatePaymentAsync.
    /// </summary>
    [HttpGet("/Payment/PaymentLinkStart/{transactionId}")]
    public async Task<IActionResult> PaymentLinkStart(string transactionId)
    {
        var tx = await _db.PaymentTransactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

        if (tx == null || string.IsNullOrEmpty(tx.ProviderData))
        {
            _log.LogWarning("PaymentLinkStart: transaction {Txn} not found or missing ProviderData",
                transactionId);
            return NotFound("Платёжная сессия не найдена или истекла.");
        }

        try
        {
            // ProviderData was stored as `{"data":"<json>"}` by PaymentService
            // when it serialised PaymentResult.ProviderData. Unwrap.
            string innerJson = tx.ProviderData;
            try
            {
                using var outer = JsonDocument.Parse(tx.ProviderData);
                if (outer.RootElement.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.String)
                    innerJson = d.GetString() ?? tx.ProviderData;
            }
            catch { /* not wrapped — use as-is */ }

            using var doc = JsonDocument.Parse(innerJson);
            var root = doc.RootElement;
            var target = root.TryGetProperty("target", out var t) ? t.GetString() : PaymentLinkPaymentProvider.TargetUrl;
            if (!root.TryGetProperty("form", out var form) || form.ValueKind != JsonValueKind.Object)
            {
                _log.LogWarning("PaymentLinkStart: ProviderData JSON has no 'form' object for {Txn}", transactionId);
                return BadRequest("Платёжные данные повреждены.");
            }

            var fields = new Dictionary<string, string>();
            foreach (var prop in form.EnumerateObject())
            {
                fields[prop.Name] = prop.Value.GetString() ?? "";
            }

            ViewData["Target"] = target;
            ViewData["Fields"] = fields;
            return View("PaymentLinkStart");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "PaymentLinkStart: failed to parse ProviderData for {Txn}", transactionId);
            return StatusCode(500, "Не удалось подготовить переход на платёжную систему.");
        }
    }
}
