using DigiVault.Core.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DigiVault.Infrastructure.Services;

public class FileService : IFileService
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<FileService> _logger;
    private readonly string _uploadsFolder;
    private readonly string _backupFolder;

    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };

    public FileService(IWebHostEnvironment environment, ILogger<FileService> logger)
    {
        _environment = environment;
        _logger = logger;

        _uploadsFolder = Path.Combine(_environment.WebRootPath, "images", "uploads");
        _backupFolder = Path.Combine(_environment.WebRootPath, "images", "backups");

        EnsureDirectoriesExist();
    }

    private void EnsureDirectoriesExist()
    {
        try
        {
            if (!Directory.Exists(_uploadsFolder))
            {
                Directory.CreateDirectory(_uploadsFolder);
                _logger.LogInformation("Created uploads directory: {Path}", _uploadsFolder);
            }

            if (!Directory.Exists(_backupFolder))
            {
                Directory.CreateDirectory(_backupFolder);
                _logger.LogInformation("Created backup directory: {Path}", _backupFolder);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create directories");
        }
    }

    public async Task<string?> SaveImageAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return null;

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
            throw new ArgumentException("Uploaded file is not a valid image");

        var fileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(_uploadsFolder, fileName);
        var backupPath = Path.Combine(_backupFolder, fileName);

        try
        {
            // Save main file
            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            // Create backup copy
            File.Copy(filePath, backupPath, true);
            _logger.LogInformation("Image saved with backup: {FileName}", fileName);

            return $"/images/uploads/{fileName}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save image: {FileName}", fileName);

            if (File.Exists(filePath))
                File.Delete(filePath);
            if (File.Exists(backupPath))
                File.Delete(backupPath);

            throw;
        }
    }

    public void DeleteImage(string? imageUrl)
    {
        if (string.IsNullOrEmpty(imageUrl))
            return;

        try
        {
            var fileName = GetFileNameFromUrl(imageUrl);
            if (string.IsNullOrEmpty(fileName))
                return;

            var fullPath = Path.Combine(_uploadsFolder, fileName);

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                _logger.LogInformation("Deleted image: {FileName}", fileName);
            }
            // Backup остаётся для возможного восстановления
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting image: {ImageUrl}", imageUrl);
        }
    }

    public async Task<bool> RestoreImageFromBackupAsync(string fileName)
    {
        try
        {
            var backupPath = Path.Combine(_backupFolder, fileName);
            var uploadPath = Path.Combine(_uploadsFolder, fileName);

            if (!File.Exists(backupPath))
            {
                _logger.LogWarning("Backup not found for: {FileName}", fileName);
                return false;
            }

            File.Copy(backupPath, uploadPath, true);
            _logger.LogInformation("Restored image from backup: {FileName}", fileName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore image: {FileName}", fileName);
            return false;
        }
    }

    private static string? GetFileNameFromUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        try
        {
            if (url.StartsWith("http://") || url.StartsWith("https://"))
            {
                var uri = new Uri(url);
                return Path.GetFileName(uri.LocalPath);
            }

            if (url.StartsWith("/images/uploads/"))
                return url.Replace("/images/uploads/", "");

            if (url.StartsWith("/images/"))
                return Path.GetFileName(url);

            return Path.GetFileName(url);
        }
        catch
        {
            return null;
        }
    }
}
