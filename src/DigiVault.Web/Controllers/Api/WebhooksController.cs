using Microsoft.AspNetCore.Mvc;

namespace DigiVault.Web.Controllers.Api;

[ApiController]
[Route("api/webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(ILogger<WebhooksController> logger)
    {
        _logger = logger;
    }

    [HttpPost("{provider}")]
    public async Task<IActionResult> HandleWebhook(string provider)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();

        _logger.LogInformation("Webhook received from {Provider}. Body length: {Length}, IP: {IP}",
            provider, body.Length, HttpContext.Connection.RemoteIpAddress);

        // Log all headers for debugging
        foreach (var header in Request.Headers.Where(h => !h.Key.StartsWith(":")))
        {
            _logger.LogDebug("Webhook header: {Key}={Value}", header.Key, header.Value);
        }

        // TODO: Route to specific provider webhook handler when providers are implemented
        // var handler = _webhookHandlerFactory.GetHandler(provider);
        // if (handler != null) return await handler.HandleAsync(body, Request.Headers);

        _logger.LogWarning("No webhook handler registered for provider: {Provider}", provider);
        return Ok(new { status = "received", provider });
    }

    [HttpGet("{provider}")]
    public IActionResult WebhookGet(string provider)
    {
        _logger.LogInformation("GET webhook request from {Provider}", provider);
        return Ok(new { status = "ok", provider });
    }
}
