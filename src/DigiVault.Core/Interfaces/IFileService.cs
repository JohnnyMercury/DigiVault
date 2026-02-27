using Microsoft.AspNetCore.Http;

namespace DigiVault.Core.Interfaces;

public interface IFileService
{
    Task<string?> SaveImageAsync(IFormFile file);
    void DeleteImage(string? imageUrl);
    Task<bool> RestoreImageFromBackupAsync(string fileName);
}
