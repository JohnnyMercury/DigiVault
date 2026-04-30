namespace DigiVault.Web.Services.Fulfilment;

/// <summary>
/// Periodic safety-net for order fulfilment. Runs every 30 seconds, sweeps any
/// Order whose items are still <see cref="DigiVault.Core.Enums.DeliveryStatus.Pending"/>
/// and tries to deliver them. Handles:
///   - App restarts mid-fulfilment
///   - Transient failures inside the in-line fulfilment call (rare - generator
///     is pure CPU; but if a future generator hits an external API and times
///     out, this will retry)
///   - Webhook-driven orders that haven't been wired to call FulfilmentService
///     directly yet (defensive)
/// </summary>
public class OrderFulfilmentBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly IServiceProvider _services;
    private readonly ILogger<OrderFulfilmentBackgroundService> _log;

    public OrderFulfilmentBackgroundService(IServiceProvider services,
        ILogger<OrderFulfilmentBackgroundService> log)
    {
        _services = services;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("OrderFulfilmentBackgroundService started; sweeping every {Interval}", Interval);

        // Small initial delay so the host can finish booting and apply migrations.
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var fulfilment = scope.ServiceProvider.GetRequiredService<IFulfilmentService>();
                await fulfilment.SweepPendingAsync(maxOrders: 50, ct: stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "OrderFulfilmentBackgroundService sweep failed");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }

        _log.LogInformation("OrderFulfilmentBackgroundService stopped");
    }
}
