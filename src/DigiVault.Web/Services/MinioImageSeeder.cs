using Minio;
using Minio.DataModel.Args;

namespace DigiVault.Web.Services;

/// <summary>
/// Uploads static images from wwwroot to MinIO on startup.
/// Ensures all bundled product/game images are available in MinIO.
/// Idempotent — skips files that already exist in the bucket.
/// </summary>
public class MinioImageSeeder : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<MinioImageSeeder> _logger;
    private readonly IWebHostEnvironment _environment;

    public MinioImageSeeder(
        IConfiguration configuration,
        ILogger<MinioImageSeeder> logger,
        IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _logger = logger;
        _environment = environment;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for MinIO to be ready
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        try
        {
            var endpoint = _configuration["MinIO:Endpoint"] ?? "localhost:9000";
            var accessKey = _configuration["MinIO:AccessKey"] ?? "minioadmin";
            var secretKey = _configuration["MinIO:SecretKey"] ?? "minioadmin";
            var useSSL = bool.Parse(_configuration["MinIO:UseSSL"] ?? "false");
            var bucketName = _configuration["MinIO:BucketName"] ?? "digivault-images";

            var clientBuilder = new MinioClient()
                .WithEndpoint(endpoint)
                .WithCredentials(accessKey, secretKey);

            if (useSSL)
                clientBuilder = clientBuilder.WithSSL();

            var minioClient = clientBuilder.Build();

            // Ensure bucket exists
            bool found = await minioClient.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(bucketName), stoppingToken);
            if (!found)
            {
                await minioClient.MakeBucketAsync(
                    new MakeBucketArgs().WithBucket(bucketName), stoppingToken);

                // Set public read policy
                var policyJson = $$"""
                {
                    "Version": "2012-10-17",
                    "Statement": [{
                        "Effect": "Allow",
                        "Principal": {"AWS": ["*"]},
                        "Action": ["s3:GetObject"],
                        "Resource": ["arn:aws:s3:::{{bucketName}}/*"]
                    }]
                }
                """;
                await minioClient.SetPolicyAsync(
                    new SetPolicyArgs().WithBucket(bucketName).WithPolicy(policyJson), stoppingToken);
            }

            // Directories to scan: images/products and images/games
            var directories = new[] { "images/products", "images/games" };
            var uploadCount = 0;

            foreach (var dir in directories)
            {
                var fullPath = Path.Combine(_environment.WebRootPath, dir);
                if (!Directory.Exists(fullPath)) continue;

                var files = Directory.GetFiles(fullPath, "*.*", SearchOption.AllDirectories)
                    .Where(IsImageFile);

                foreach (var file in files)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    // Get relative path from wwwroot: "images/products/fortnite/1000.webp"
                    var relativePath = Path.GetRelativePath(_environment.WebRootPath, file)
                        .Replace('\\', '/');

                    // MinIO object key: strip "images/" prefix → "products/fortnite/1000.webp"
                    var objectKey = relativePath.StartsWith("images/")
                        ? relativePath[7..]
                        : relativePath;

                    // Check if already exists in MinIO
                    try
                    {
                        await minioClient.StatObjectAsync(
                            new StatObjectArgs().WithBucket(bucketName).WithObject(objectKey), stoppingToken);
                        continue; // Already exists
                    }
                    catch (Minio.Exceptions.ObjectNotFoundException)
                    {
                        // Not found — upload it
                    }

                    var contentType = GetContentType(Path.GetExtension(file));
                    await using var stream = File.OpenRead(file);

                    await minioClient.PutObjectAsync(new PutObjectArgs()
                        .WithBucket(bucketName)
                        .WithObject(objectKey)
                        .WithStreamData(stream)
                        .WithObjectSize(stream.Length)
                        .WithContentType(contentType), stoppingToken);

                    uploadCount++;
                }
            }

            if (uploadCount > 0)
                _logger.LogInformation("MinIO image seeder: uploaded {Count} static images", uploadCount);
            else
                _logger.LogInformation("MinIO image seeder: all static images already in MinIO");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MinIO image seeder failed");
        }
    }

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".svg" or ".avif" or ".ico";
    }

    private static string GetContentType(string ext) => ext.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".svg" => "image/svg+xml",
        ".avif" => "image/avif",
        ".ico" => "image/x-icon",
        _ => "application/octet-stream"
    };
}
