using DigiVault.Web.Services.Payment;
using Microsoft.Extensions.Options;
using Xunit;

namespace DigiVault.Web.Tests.Payment;

public class PaymentAnonymizerUserIdTests
{
    private const string WhitelistedEmail = "test-account@example.com";
    private const string RealUserId = "8f9c1e2a-1111-2222-3333-444455556666";

    private static PaymentAnonymizer BuildAnonymizer()
        => new(Options.Create(new PaymentAnonymizationOptions
        {
            Emails = new List<string> { WhitelistedEmail },
        }));

    [Fact]
    public void AnonymizeUserId_passes_through_for_non_whitelisted_email()
    {
        var anonymizer = BuildAnonymizer();

        var result = anonymizer.AnonymizeUserId("real-customer@gmail.com", RealUserId);

        Assert.Equal(RealUserId, result);
    }

    [Fact]
    public void AnonymizeUserId_returns_fresh_guid_shaped_id_for_whitelisted_email()
    {
        var anonymizer = BuildAnonymizer();

        var first  = anonymizer.AnonymizeUserId(WhitelistedEmail, RealUserId);
        var second = anonymizer.AnonymizeUserId(WhitelistedEmail, RealUserId);

        // Never the real id, a different value each call, and shaped like a
        // real ASP.NET Identity user id (a parseable GUID) so it blends in.
        Assert.NotEqual(RealUserId, first);
        Assert.NotEqual(first, second);
        Assert.True(Guid.TryParse(first, out _));
        Assert.True(Guid.TryParse(second, out _));
    }

    [Fact]
    public void AnonymizeUserId_matches_email_case_insensitively()
    {
        var anonymizer = BuildAnonymizer();

        var result = anonymizer.AnonymizeUserId(WhitelistedEmail.ToUpperInvariant(), RealUserId);

        Assert.NotEqual(RealUserId, result);
    }
}
