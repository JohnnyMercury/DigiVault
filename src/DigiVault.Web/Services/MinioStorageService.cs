using DigiVault.Core.Interfaces;
using Minio;
using Minio.DataModel.Args;

namespace DigiVault.Web.Services;

public class MinioStorageService : IFileService
{
    private readonly IMinioClient _minioClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MinioStorageService> _logger;
    private readonly string _bucketName;
    private readonly string _backupBucketName;
    private readonly bool _useRelativeUrls;

    public MinioStorageService(
        IConfiguration configuration,
        ILogger<MinioStorageService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _bucketName = _configuration["MinIO:BucketName"] ?? "digivault-images";
        _backupBucketName = $"{_bucketName}-backup";
        _useRelativeUrls = _configuration.GetValue("MinIO:UseRelativeUrls", true);

        var endpoint = _configuration["MinIO:Endpoint"] ?? "localhost:9000";
        var accessKey = _configuration["MinIO:AccessKey"] ?? "minioadmin";
        var secretKey = _configuration["MinIO:SecretKey"] ?? "minioadmin";
        var useSSL = bool.Parse(_configuration["MinIO:UseSSL"] ?? "false");

        var client = new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(accessKey, secretKey);

        if (useSSL)
            client = client.WithSSL();

        _minioClient = client.Build();

        Task.Run(async () =>
        {
            await EnsureBucketExistsAsync(_bucketName);
            await EnsureBucketExistsAsync(_backupBucketName);
        });
    }

    private async Task EnsureBucketExistsAsync(string bucketName)
    {
        try
        {
            bool found = await _minioClient.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(bucketName));

            if (!found)
            {
                await _minioClient.MakeBucketAsync(
                    new MakeBucketArgs().WithBucket(bucketName));

                if (bucketName == _bucketName)
                {
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
                    await _minioClient.SetPolicyAsync(
                        new SetPolicyArgs().WithBucket(bucketName).WithPolicy(policyJson));
                }

                _logger.LogInformation("Created MinIO bucket: {BucketName}", bucketName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure MinIO bucket exists: {BucketName}", bucketName);
        }
    }

    public async Task<string?> SaveImageAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return null;

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!IsImageExtension(extension))
            throw new ArgumentException("Uploaded file is not an image");

        var fileName = $"{Guid.NewGuid()}{extension}";
        var contentType = GetContentType(extension);

        try
        {
            using var stream = file.OpenReadStream();

            await _minioClient.PutObjectAsync(new PutObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName)
                .WithStreamData(stream)
                .WithObjectSize(file.Length)
                .WithContentType(contentType));

            // Save backup copy
            stream.Position = 0;
            await _minioClient.PutObjectAsync(new PutObjectArgs()
                .WithBucket(_backupBucketName)
                .WithObject(fileName)
                .WithStreamData(stream)
                .WithObjectSize(file.Length)
                .WithContentType(contentType));

            _logger.LogInformation("Image uploaded: {FileName}", fileName);

            return _useRelativeUrls
                ? $"/minio/{_bucketName}/{fileName}"
                : $"{_configuration["MinIO:PublicUrl"] ?? "http://localhost:9000"}/{_bucketName}/{fileName}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload image: {FileName}", fileName);
            throw;
        }
    }

    public void DeleteImage(string? fileUrl)
    {
        if (string.IsNullOrEmpty(fileUrl)) return;
        Task.Run(async () =>
        {
            try
            {
                var fileName = ExtractFileName(fileUrl);
                if (string.IsNullOrEmpty(fileName)) return;

                await _minioClient.RemoveObjectAsync(new RemoveObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(fileName));
                _logger.LogInformation("Image deleted: {FileName}", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete image: {Url}", fileUrl);
            }
        });
    }

    public async Task<bool> RestoreImageFromBackupAsync(string fileName)
    {
        try
        {
            await _minioClient.CopyObjectAsync(new CopyObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName)
                .WithCopyObjectSource(new CopySourceObjectArgs()
                    .WithBucket(_backupBucketName)
                    .WithObject(fileName)));

            _logger.LogInformation("Restored image from backup: {FileName}", fileName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore image: {FileName}", fileName);
            return false;
        }
    }

    private static string? ExtractFileName(string url)
    {
        if (url.StartsWith("/minio/"))
        {
            var parts = url.Split('/');
            return parts.Length >= 4 ? parts[3] : null;
        }
        if (url.Contains("://"))
        {
            var uri = new Uri(url);
            return uri.Segments.Length >= 3 ? uri.Segments[^1] : null;
        }
        return Path.GetFileName(url);
    }

    private static bool IsImageExtension(string ext) =>
        new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" }.Contains(ext);

    private static string GetContentType(string ext) => ext switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        _ => "application/octet-stream"
    };
}
