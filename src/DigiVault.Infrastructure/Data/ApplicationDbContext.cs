using DigiVault.Core.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductKey> ProductKeys => Set<ProductKey>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<PaymentProviderConfig> PaymentProviderConfigs => Set<PaymentProviderConfig>();

    // Games
    public DbSet<Game> Games => Set<Game>();
    public DbSet<GameProduct> GameProducts => Set<GameProduct>();

    // Gift Cards
    public DbSet<GiftCard> GiftCards => Set<GiftCard>();

    // VPN Providers
    public DbSet<VpnProvider> VpnProviders => Set<VpnProvider>();

    // Hero Banners
    public DbSet<HeroBanner> HeroBanners => Set<HeroBanner>();

    // Email & Wallet
    public DbSet<EmailVerificationCode> EmailVerificationCodes => Set<EmailVerificationCode>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ApplicationUser configuration
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(e => e.Balance).HasPrecision(18, 2);
        });

        // Product configuration
        builder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.Price).HasPrecision(18, 2);
            entity.Property(e => e.OldPrice).HasPrecision(18, 2);
            entity.Property(e => e.ImageUrl).HasMaxLength(500);
            entity.Property(e => e.Metadata).HasMaxLength(1000);
            entity.HasIndex(e => e.Category);
            entity.HasIndex(e => e.IsActive);
        });

        // ProductKey configuration
        builder.Entity<ProductKey>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.KeyValue).HasMaxLength(500).IsRequired();
            entity.HasOne(e => e.GameProduct)
                .WithMany(gp => gp.ProductKeys)
                .HasForeignKey(e => e.GameProductId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.OrderItem)
                .WithMany(oi => oi.ProductKeys)
                .HasForeignKey(e => e.OrderItemId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => e.IsUsed);
        });

        // Order configuration
        builder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OrderNumber).HasMaxLength(50).IsRequired();
            entity.Property(e => e.TotalAmount).HasPrecision(18, 2);
            entity.Property(e => e.DeliveryInfo).HasMaxLength(500);
            entity.HasOne(e => e.User)
                .WithMany(u => u.Orders)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.OrderNumber).IsUnique();
            entity.HasIndex(e => e.Status);
        });

        // OrderItem configuration
        builder.Entity<OrderItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UnitPrice).HasPrecision(18, 2);
            entity.Property(e => e.TotalPrice).HasPrecision(18, 2);
            entity.HasOne(e => e.Order)
                .WithMany(o => o.OrderItems)
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.GameProduct)
                .WithMany(gp => gp.OrderItems)
                .HasForeignKey(e => e.GameProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // CartItem configuration
        builder.Entity<CartItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.User)
                .WithMany(u => u.CartItems)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Product)
                .WithMany()
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.UserId, e.ProductId }).IsUnique();
        });

        // Transaction configuration
        builder.Entity<Transaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.HasOne(e => e.User)
                .WithMany(u => u.Transactions)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Order)
                .WithMany()
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => e.Type);
            entity.HasIndex(e => e.CreatedAt);
        });

        // PaymentTransaction configuration
        builder.Entity<PaymentTransaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TransactionId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ProviderTransactionId).HasMaxLength(200);
            entity.Property(e => e.ProviderName).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.Currency).HasMaxLength(10);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.ErrorMessage).HasMaxLength(1000);
            entity.Property(e => e.ClientIp).HasMaxLength(50);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Order)
                .WithMany()
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => e.TransactionId).IsUnique();
            entity.HasIndex(e => e.ProviderTransactionId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);
        });

        // PaymentProviderConfig configuration
        builder.Entity<PaymentProviderConfig>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(50).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ApiKey).HasMaxLength(500);
            entity.Property(e => e.SecretKey).HasMaxLength(500);
            entity.Property(e => e.MerchantId).HasMaxLength(100);
            entity.Property(e => e.Commission).HasPrecision(5, 2);
            entity.Property(e => e.MinAmount).HasPrecision(18, 2);
            entity.Property(e => e.MaxAmount).HasPrecision(18, 2);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasIndex(e => e.IsEnabled);
        });

        // Game configuration
        builder.Entity<Game>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Slug).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Subtitle).HasMaxLength(100);
            entity.Property(e => e.Currency).HasMaxLength(50);
            entity.Property(e => e.CurrencyShort).HasMaxLength(20);
            entity.Property(e => e.ImageUrl).HasMaxLength(500);
            entity.Property(e => e.IconUrl).HasMaxLength(500);
            entity.Property(e => e.Icon).HasMaxLength(10);
            entity.Property(e => e.Gradient).HasMaxLength(200);
            entity.Property(e => e.Color).HasMaxLength(20);
            entity.HasIndex(e => e.Slug).IsUnique();
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.SortOrder);
        });

        // GameProduct configuration
        builder.Entity<GameProduct>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Amount).HasMaxLength(50);
            entity.Property(e => e.Bonus).HasMaxLength(50);
            entity.Property(e => e.TotalDisplay).HasMaxLength(100);
            entity.Property(e => e.Price).HasPrecision(18, 2);
            entity.Property(e => e.OldPrice).HasPrecision(18, 2);
            entity.Property(e => e.Multiplier).HasMaxLength(20);
            entity.Property(e => e.Region).HasMaxLength(20);
            entity.Property(e => e.ImageUrl).HasMaxLength(500);
            entity.HasOne(e => e.Game)
                .WithMany(g => g.Products)
                .HasForeignKey(e => e.GameId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.GiftCard)
                .WithMany(g => g.Products)
                .HasForeignKey(e => e.GiftCardId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.VpnProvider)
                .WithMany(v => v.Products)
                .HasForeignKey(e => e.VpnProviderId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.GameId);
            entity.HasIndex(e => e.GiftCardId);
            entity.HasIndex(e => e.VpnProviderId);
            entity.HasIndex(e => e.ProductType);
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.SortOrder);
        });

        // GiftCard configuration
        builder.Entity<GiftCard>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Slug).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.ImageUrl).HasMaxLength(500);
            entity.Property(e => e.Icon).HasMaxLength(10);
            entity.Property(e => e.Gradient).HasMaxLength(200);
            entity.HasIndex(e => e.Slug).IsUnique();
            entity.HasIndex(e => e.Category);
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.SortOrder);
        });

        // VpnProvider configuration
        builder.Entity<VpnProvider>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Slug).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Tagline).HasMaxLength(200);
            entity.Property(e => e.Features).HasMaxLength(2000);
            entity.Property(e => e.ImageUrl).HasMaxLength(500);
            entity.Property(e => e.Icon).HasMaxLength(10);
            entity.Property(e => e.Gradient).HasMaxLength(200);
            entity.HasIndex(e => e.Slug).IsUnique();
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.SortOrder);
        });

        // HeroBanner configuration
        builder.Entity<HeroBanner>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Subtitle).HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.ButtonText).HasMaxLength(100);
            entity.Property(e => e.ButtonUrl).HasMaxLength(500);
            entity.Property(e => e.ImageUrl).HasMaxLength(500);
            entity.Property(e => e.Gradient).HasMaxLength(300);
            entity.Property(e => e.SubtitleColor).HasMaxLength(50);
            entity.Property(e => e.ButtonClass).HasMaxLength(50);
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.SortOrder);
        });

        // EmailVerificationCode configuration
        builder.Entity<EmailVerificationCode>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).HasMaxLength(10).IsRequired();
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.UserId, e.IsUsed });
        });

        // WalletTransaction configuration
        builder.Entity<WalletTransaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.BalanceAfterTransaction).HasPrecision(18, 2);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Reference).HasMaxLength(200);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Type);
            entity.HasIndex(e => e.CreatedAt);
        });
    }
}
