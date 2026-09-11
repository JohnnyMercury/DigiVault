using Xunit;

namespace DigiVault.Web.Tests.Security;

public class PikeDomainVerificationPageTests
{
    [Fact]
    public void Pike_domain_verification_page_contains_the_required_ownership_statement()
    {
        var pagePath = Path.Combine(FindRepositoryRoot(), "src", "DigiVault.Web", "wwwroot", "pike-verification.html");

        Assert.True(File.Exists(pagePath), "The Pike verification page must be publicly deployed.");
        Assert.Contains(
            "This page is created in order to confirm that the domain is owned and operated by the Sole Trader ROZIEV ISFANDIYOR UMIDZHON UGLI applying for integration with Pike Payment Services Provider.",
            File.ReadAllText(pagePath));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DigiVault.sln")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the DigiVault repository root.");
    }
}
