using DigiVault.Infrastructure.Data;
using Xunit;

namespace DigiVault.Web.Tests.Payment;

public class ParityPaySeedDefaultsTests
{
    [Fact]
    public void CreateParityPayProviderConfig_returns_visible_provider_for_picker()
    {
        var now = new DateTime(2026, 7, 9, 12, 0, 0, DateTimeKind.Utc);

        var config = DbSeeder.CreateParityPayProviderConfig(now);

        Assert.Equal("paritypay", config.Name);
        Assert.Equal("Kizona", config.DisplayName);
        Assert.True(config.IsEnabled);
        Assert.Equal("{\"baseUrl\":\"https://api.paritypay.ru\",\"expireMinutes\":60}", config.Settings);
        Assert.Equal(now, config.CreatedAt);
        Assert.Equal(now, config.UpdatedAt);
    }
}
