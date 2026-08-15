using DigiVault.Core.Enums;
using DigiVault.Web.Services.Payment;
using DigiVault.Web.Services.Payment.Providers.Antimatter;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigiVault.Web.Tests.Payment;

public class AntimatterPaymentProviderTests
{
    [Fact]
    public void Supports_sbp_instead_of_bank_card()
    {
        var provider = new AntimatterPaymentProvider(
            db: null!,
            httpFactory: null!,
            anonymizer: null!,
            log: NullLogger<AntimatterPaymentProvider>.Instance);

        Assert.Contains(PaymentMethod.SBP, provider.SupportedMethods);
        Assert.DoesNotContain(PaymentMethod.Card, provider.SupportedMethods);
    }
}
