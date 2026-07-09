using System.Text.Json;
using DigiVault.Web.Services.Payment.Providers.ParityPay;
using Xunit;

namespace DigiVault.Web.Tests.Payment;

public class ParityPaySignatureHelperTests
{
    [Fact]
    public void BuildSignature_sorts_request_parameters_by_key_and_concatenates_values()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["shop_id"] = "9bee9309-2585-4332-b63d-e1d897f7ce84",
            ["amount"] = 5000,
            ["order_id"] = "123111123",
        };

        var signature = ParityPaySignatureHelper.BuildSignature(
            parameters,
            "your_secret_api_key");

        Assert.Equal(
            "b4ed0c181e8f904a006a62803912047d727dfdefde612ba2061af693874aae17",
            signature);
    }

    [Fact]
    public void BuildSignature_treats_webhook_null_values_as_empty_strings()
    {
        using var doc = JsonDocument.Parse("""
        {
            "id": "9beea835-0937-4b5c-8f5a-c3a0d0e60346",
            "amount": "1250.00",
            "status": "PAID",
            "comment": "test",
            "created": "2024-01-30 19:07:45",
            "expires": "2024-01-30 20:07:45",
            "service": "sbp",
            "shop_id": "9bee9309-2585-4332-b63d-e1d897f7ce84",
            "order_id": "11113",
            "custom_fields": null
        }
        """);

        var signature = ParityPaySignatureHelper.BuildSignature(
            doc.RootElement,
            "webhook_secret");

        Assert.Equal(
            "8c77c25565383165787e8f3b9c5601a4e5aacc8393ea4ebcb5b4aa773cd3301d",
            signature);
    }
}
