using Microsoft.AspNetCore.Identity;

namespace DigiVault.Core.Entities;

public class ApplicationUser : IdentityUser
{
    public decimal Balance { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;

    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    public virtual ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
}
