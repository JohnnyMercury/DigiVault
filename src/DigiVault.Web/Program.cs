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

// Payment infrastructure
builder.Services.AddScoped<IPaymentProvider, TestPaymentProvider>();
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
}

var app = builder.Build();

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
