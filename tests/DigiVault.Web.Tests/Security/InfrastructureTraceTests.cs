using Xunit;

namespace DigiVault.Web.Tests.Security;

public class InfrastructureTraceTests
{
    private const string LegacyServerAddress = "145" + ".223" + ".90" + ".75";

    [Fact]
    public void Tracked_source_and_deployment_files_do_not_embed_the_legacy_server_address()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceFiles = Directory.EnumerateFiles(repositoryRoot, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(repositoryRoot, "docker-compose*.yml", SearchOption.TopDirectoryOnly))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        foreach (var path in sourceFiles)
        {
            var contents = File.ReadAllText(path);
            Assert.DoesNotContain(LegacyServerAddress, contents);
        }
    }

    [Fact]
    public void Production_compose_uses_environment_variables_for_deployment_secrets()
    {
        var compose = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docker-compose.prod.yml"));

        Assert.DoesNotContain("ConnectionStrings__DefaultConnection=Host=", compose);
        Assert.DoesNotContain("MinIO__AccessKey=minioadmin", compose);
        Assert.DoesNotContain("MinIO__PublicUrl=https://", compose);

        Assert.Contains("ConnectionStrings__DefaultConnection=${DIGIVAULT_CONNECTION_STRING", compose);
        Assert.Contains("MinIO__AccessKey=${MINIO_ACCESS_KEY", compose);
        Assert.Contains("MinIO__SecretKey=${MINIO_SECRET_KEY", compose);
        Assert.Contains("MinIO__PublicUrl=${MINIO_PUBLIC_URL", compose);
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
