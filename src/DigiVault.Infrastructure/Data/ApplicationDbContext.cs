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
            entity.HasOne(e => e.Product)
                .WithMany(p => p.ProductKeys)
                .HasForeignKey(e => e.ProductId)
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
            entity.HasOne(e => e.Product)
                .WithMany(p => p.OrderItems)
                .HasForeignKey(e => e.ProductId)
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
    }
}
