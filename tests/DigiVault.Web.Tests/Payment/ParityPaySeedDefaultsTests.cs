using DigiVault.Infrastructure.Data;
using Xunit;

namespace DigiVault.Web.Tests.Payment;

public class ParityPaySeedDefaultsTests
{
    [Fact]
    public void CreateParityPayProviderConfig_returns_disabled_provider_until_credentials_are_set()
    {
        var now = new DateTime(2026, 7, 9, 12, 0, 0, DateTimeKind.Utc);

        var config = DbSeeder.CreateParityPayProviderConfig(now);

        Assert.Equal("paritypay", config.Name);
        Assert.Equal("ParityPay", config.DisplayName);
        Assert.False(config.IsEnabled);
        Assert.Equal("{\"baseUrl\":\"https://api.paritypay.ru\",\"expireMinutes\":60}", config.Settings);
        Assert.Equal(now, config.CreatedAt);
        Assert.Equal(now, config.UpdatedAt);
    }

    [Fact]
    public void HasParityPayProviderCredentials_requires_all_three_secrets()
    {
        var config = DbSeeder.CreateParityPayProviderConfig(DateTime.UtcNow);

        Assert.False(DbSeeder.HasParityPayProviderCredentials(config));

        config.MerchantId = "shop-id";
        config.ApiKey = "api-key";
        config.SecretKey = "webhook-key";

        Assert.True(DbSeeder.HasParityPayProviderCredentials(config));
    }
}
