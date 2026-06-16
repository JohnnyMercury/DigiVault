using DigiVault.Core.Entities;
using DigiVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Services;

/// <summary>
/// Centralised toggle for runtime-controlled visibility of catalog sections.
/// Backed by rows in the AppSettings table (key `vpn:visible` etc.), so an
/// admin can flip them without a redeploy — used by Key Zona to temporarily
/// hide the VPN catalog from acquirer reviewers during PSP onboarding.
/// </summary>
public interface IFeatureFlagsService
{
    Task<bool> IsVpnVisibleAsync();
    Task SetVpnVisibleAsync(bool visible);
}

public class FeatureFlagsService : IFeatureFlagsService
{
    public const string KeyVpnVisible = "vpn:visible";

    private readonly ApplicationDbContext _context;

    public FeatureFlagsService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<bool> IsVpnVisibleAsync()
    {
        var row = await _context.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == KeyVpnVisible);
        if (row == null) return true;  // default visible
        return !string.Equals(row.Value, "false", StringComparison.OrdinalIgnoreCase);
    }

    public async Task SetVpnVisibleAsync(bool visible)
    {
        var row = await _context.AppSettings.FirstOrDefaultAsync(s => s.Key == KeyVpnVisible);
        var stringValue = visible ? "true" : "false";
        if (row != null)
        {
            row.Value = stringValue;
            row.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _context.AppSettings.Add(new AppSetting
            {
                Key = KeyVpnVisible,
                Value = stringValue,
                Description = "Public visibility of VPN catalog/menu/reviews (admin toggle)",
                UpdatedAt = DateTime.UtcNow,
            });
        }
        await _context.SaveChangesAsync();
    }
}
