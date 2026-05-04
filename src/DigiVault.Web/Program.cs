using DigiVault.Core.Entities;
using DigiVault.Core.Interfaces;
using DigiVault.Infrastructure.Data;
using DigiVault.Infrastructure.Services;
using DigiVault.Web.Services.Payment;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Configure PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// Configure Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// Configure cookie settings
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
});

// Add session support
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Register application services
builder.Services.AddScoped<DigiVault.Web.Services.IOrderService, DigiVault.Web.Services.OrderService>();
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<IGameService, GameService>();

// Fulfilment — generates the actual delivered credentials for paid orders.
builder.Services.AddScoped<DigiVault.Web.Services.Fulfilment.ICredentialGenerator,
                          DigiVault.Web.Services.Fulfilment.CredentialGenerator>();
builder.Services.AddScoped<DigiVault.Web.Services.Fulfilment.IFulfilmentService,
                          DigiVault.Web.Services.Fulfilment.FulfilmentService>();
builder.Services.AddHostedService<DigiVault.Web.Services.Fulfilment.OrderFulfilmentBackgroundService>();

// Payment infrastructure
builder.Services.AddHttpClient(); // for Enot/other providers' outbound HTTP
// NOTE: TestPaymentProvider is NOT registered. It auto-approves payments and
// would shadow Enot for Card / SBP since IPaymentProviderFactory.GetProviderForMethod
// just returns the first match. Bring it back only inside `if (env.IsDevelopment())`
// when you actually need the test fallback.
builder.Services.AddScoped<IPaymentProvider, DigiVault.Web.Services.Payment.Providers.Enot.EnotPaymentProvider>();
builder.Services.AddScoped<IPaymentProvider, DigiVault.Web.Services.Payment.Providers.PaymentLink.PaymentLinkPaymentProvider>();
builder.Services.AddScoped<IPaymentProvider, DigiVault.Web.Services.Payment.Providers.Overpay.OverpayPaymentProvider>();

// Named HttpClient for Overpay - it requires mTLS (client certificate). The
// p12 file path + passphrase are read from PaymentProviderConfig.Settings
// (admin-editable). When admin doesn't supply a cert, the handler falls back
// to a plain client (the provider itself logs «cert not configured» on first
// outbound call). Lifetime = 5 min so cert rotations on disk get picked up
// without app restart.
builder.Services.AddHttpClient(
    "overpay",
    client => { client.Timeout = TimeSpan.FromSeconds(30); })
    .ConfigurePrimaryHttpMessageHandler(sp =>
    {
        var handler = new HttpClientHandler();
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider
                .GetRequiredService<DigiVault.Infrastructure.Data.ApplicationDbContext>();
            var cfg = db.PaymentProviderConfigs
                .AsNoTracking()
                .FirstOrDefault(c => c.Name == "overpay");

            if (cfg != null && !string.IsNullOrWhiteSpace(cfg.Settings))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(cfg.Settings);
                var root = doc.RootElement;
                var path = root.TryGetProperty("certPath", out var p) ? p.GetString() : null;
                var pass = root.TryGetProperty("certPass", out var ps) ? ps.GetString() : null;

                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                        path,
                        pass ?? "",
                        System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.MachineKeySet
                        | System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.PersistKeySet
                        | System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.Exportable);
                    handler.ClientCertificates.Add(cert);
                }
            }
        }
        catch (Exception ex)
        {
            // Don't crash startup just because cert is missing - the provider
            // returns a friendly error on first call.
            sp.GetService<ILogger<Program>>()?
                .LogWarning(ex, "Overpay client cert load failed; mTLS calls will be rejected by upstream");
        }
        return handler;
    })
    .SetHandlerLifetime(TimeSpan.FromMinutes(5));

// TODO: Add real providers here:
// builder.Services.AddScoped<IPaymentProvider, YooKassaProvider>();
// builder.Services.AddScoped<IPaymentProvider, StripeProvider>();
builder.Services.AddScoped<IPaymentProviderFactory, PaymentProviderFactory>();
builder.Services.AddScoped<DigiVault.Web.Services.IPaymentService, DigiVault.Web.Services.PaymentService>();

// Email & Verification
builder.Services.AddScoped<DigiVault.Web.Services.IEmailService, DigiVault.Web.Services.EmailService>();
builder.Services.AddScoped<DigiVault.Web.Services.IEmailVerificationService, DigiVault.Web.Services.EmailVerificationService>();

// Balance
builder.Services.AddScoped<DigiVault.Web.Services.IBalanceService, DigiVault.Web.Services.BalanceService>();

// Database & Backup admin services
builder.Services.AddScoped<DigiVault.Web.Services.IDatabaseService, DigiVault.Web.Services.DatabaseService>();
builder.Services.AddScoped<DigiVault.Web.Services.IBackupService, DigiVault.Web.Services.BackupService>();

// MinIO storage (conditional)
if (builder.Configuration.GetValue<bool>("Storage:UseMinIO", false))
{
    builder.Services.AddScoped<IFileService, DigiVault.Web.Services.MinioStorageService>();
    builder.Services.AddHostedService<DigiVault.Web.Services.MinioImageSeeder>();
}

// Trust X-Forwarded-* headers from the reverse proxy (nginx/Cloudflare) so
// Request.Scheme reflects the public protocol (https) rather than the proxy→
// app hop (http). Without this, every Url.Action / $"{Request.Scheme}://..."
// produces http:// links — which then trip browser warnings, mixed-content
// blocks, and PaymentLink/Enot/Overpay returning users via insecure URLs.
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(opts =>
{
    opts.ForwardedHeaders =
        Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
        Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto |
        Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost;
    // The default known-network / known-proxy lists exclude Docker bridge IPs
    // and Cloudflare ranges, so the headers from nginx-in-the-same-compose
    // would be ignored. Clear the lists to trust whatever the proxy injects;
    // safe because the app port (8082) is not exposed to the public internet.
    opts.KnownNetworks.Clear();
    opts.KnownProxies.Clear();
});

var app = builder.Build();

// Must run before anything that reads Request.Scheme/Host (auth cookies,
// HTTPS redirect, generated URLs in views, payment-provider callback URLs).
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// MinIO proxy middleware (before static files)
if (app.Configuration.GetValue<bool>("Storage:UseMinIO", false))
{
    app.UseMiddleware<DigiVault.Web.Middleware.MinioProxyMiddleware>();
}

app.UseStaticFiles();

app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "catalog-game",
    pattern: "Catalog/Game/{slug}",
    defaults: new { controller = "Catalog", action = "Game" });

app.MapControllerRoute(
    name: "catalog-giftcard",
    pattern: "Catalog/GiftCard/{slug}",
    defaults: new { controller = "Catalog", action = "GiftCard" });

app.MapControllerRoute(
    name: "catalog-vpnprovider",
    pattern: "Catalog/VpnProvider/{slug}",
    defaults: new { controller = "Catalog", action = "VpnProvider" });

app.MapControllerRoute(
    name: "catalog-telegram",
    pattern: "Catalog/Telegram",
    defaults: new { controller = "Catalog", action = "Telegram" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Apply migrations and seed database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();

    // Seed games data (only if no games exist)
    var gameService = scope.ServiceProvider.GetRequiredService<IGameService>();
    await gameService.SeedDefaultGamesAsync();
}
await DbSeeder.SeedAsync(app.Services);

app.Run();
