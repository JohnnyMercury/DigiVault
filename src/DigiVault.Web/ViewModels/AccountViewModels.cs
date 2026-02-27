using System.ComponentModel.DataAnnotations;

namespace DigiVault.Web.ViewModels;

public class ProfileViewModel
{
    public string Email { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public int TotalOrders { get; set; }
    public decimal TotalSpent { get; set; }
}

public class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Текущий пароль обязателен")]
    [DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Новый пароль обязателен")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Пароль должен быть минимум 6 символов")]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Подтвердите новый пароль")]
    [DataType(DataType.Password)]
    [Compare("NewPassword", ErrorMessage = "Пароли не совпадают")]
    public string ConfirmNewPassword { get; set; } = string.Empty;
}
