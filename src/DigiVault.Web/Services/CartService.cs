using DigiVault.Core.Entities;
using DigiVault.Infrastructure.Data;
using DigiVault.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Services;

public interface ICartService
{
    Task<CartViewModel> GetCartAsync(string userId);
    Task<bool> AddToCartAsync(string userId, int productId, int quantity = 1);
    Task<bool> UpdateQuantityAsync(string userId, int productId, int quantity);
    Task<bool> RemoveFromCartAsync(string userId, int productId);
    Task ClearCartAsync(string userId);
    Task<int> GetCartItemCountAsync(string userId);
}

public class CartService : ICartService
{
    private readonly ApplicationDbContext _context;

    public CartService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CartViewModel> GetCartAsync(string userId)
    {
        var cartItems = await _context.CartItems
            .Include(c => c.Product)
            .Where(c => c.UserId == userId)
            .ToListAsync();

        return new CartViewModel
        {
            Items = cartItems.Select(c => new CartItemViewModel
            {
                ProductId = c.ProductId,
                ProductName = c.Product.Name,
                ImageUrl = c.Product.ImageUrl,
                UnitPrice = c.Product.Price,
                Quantity = c.Quantity,
                MaxQuantity = c.Product.StockQuantity
            }).ToList()
        };
    }

    public async Task<bool> AddToCartAsync(string userId, int productId, int quantity = 1)
    {
        var product = await _context.Products.FindAsync(productId);
        if (product == null || !product.IsActive || product.StockQuantity < quantity)
            return false;

        var existingItem = await _context.CartItems
            .FirstOrDefaultAsync(c => c.UserId == userId && c.ProductId == productId);

        if (existingItem != null)
        {
            var newQuantity = existingItem.Quantity + quantity;
            if (newQuantity > product.StockQuantity)
                newQuantity = product.StockQuantity;
            existingItem.Quantity = newQuantity;
        }
        else
        {
            _context.CartItems.Add(new CartItem
            {
                UserId = userId,
                ProductId = productId,
                Quantity = quantity
            });
        }

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateQuantityAsync(string userId, int productId, int quantity)
    {
        if (quantity <= 0)
            return await RemoveFromCartAsync(userId, productId);

        var cartItem = await _context.CartItems
            .Include(c => c.Product)
            .FirstOrDefaultAsync(c => c.UserId == userId && c.ProductId == productId);

        if (cartItem == null)
            return false;

        if (quantity > cartItem.Product.StockQuantity)
            quantity = cartItem.Product.StockQuantity;

        cartItem.Quantity = quantity;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RemoveFromCartAsync(string userId, int productId)
    {
        var cartItem = await _context.CartItems
            .FirstOrDefaultAsync(c => c.UserId == userId && c.ProductId == productId);

        if (cartItem == null)
            return false;

        _context.CartItems.Remove(cartItem);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task ClearCartAsync(string userId)
    {
        var cartItems = await _context.CartItems
            .Where(c => c.UserId == userId)
            .ToListAsync();

        _context.CartItems.RemoveRange(cartItems);
        await _context.SaveChangesAsync();
    }

    public async Task<int> GetCartItemCountAsync(string userId)
    {
        return await _context.CartItems
            .Where(c => c.UserId == userId)
            .SumAsync(c => c.Quantity);
    }
}
