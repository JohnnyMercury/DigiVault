using Xunit;

namespace DigiVault.Web.Tests.Security;

public class LegalDisclosureTests
{
    [Fact]
    public void Legal_pages_publish_registration_consent_and_fraud_prevention_information()
    {
        var root = FindRepositoryRoot();

        var agreement = File.ReadAllText(Path.Combine(root, "src", "DigiVault.Web", "Views", "Home", "UserAgreement.cshtml"));
        var privacy = File.ReadAllText(Path.Combine(root, "src", "DigiVault.Web", "Views", "Home", "Privacy.cshtml"));
        var registration = File.ReadAllText(Path.Combine(root, "src", "DigiVault.Web", "Views", "Account", "Register.cshtml"));
        var consentPath = Path.Combine(root, "src", "DigiVault.Web", "Views", "Home", "PersonalDataConsent.cshtml");

        Assert.Contains("Центр государственных услуг города Ферганы", agreement);
        Assert.True(File.Exists(consentPath), "The personal-data consent page must be published.");
        Assert.Contains("Согласие на обработку персональных данных", File.ReadAllText(consentPath));
        Assert.Contains("PersonalDataConsent", registration);
        Assert.Contains("меры по снижению и контролю рисков мошеннических операций", privacy);
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
