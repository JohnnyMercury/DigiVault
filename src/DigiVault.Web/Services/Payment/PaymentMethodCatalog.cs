using DigiVault.Core.Enums;

namespace DigiVault.Web.Services.Payment;

/// <summary>
/// Single source of truth for the list of top-level payment methods rendered
/// across the site (modals on Game/Telegram/GiftCard/VpnProvider, Account
/// /Deposit, future cart-checkout pages, …).
///
/// To add / remove / rename a method, edit one record in <see cref="All"/>.
/// The shared partial <c>_PaymentMethodTiles.cshtml</c> renders tiles from
/// this catalog; <c>OrderService</c> and <c>AccountController.Deposit</c>
/// translate the same code to the <c>PaymentMethod</c> enum via
/// <see cref="ToEnum"/>.
///
/// `Available = false` means the method is shown to the user with a «Скоро»
/// pill - the click is intercepted with an inline notice. Flip it to true
/// when a PSP that supports the method is registered in DI.
/// </summary>
public sealed record PaymentMethodOption(
    string Code,
    string Title,
    string Description,
    string IconClass,
    string Gradient,
    PaymentMethod EnumValue,
    bool Available,
    string? UnavailableHint = null
);

public static class PaymentMethodCatalog
{
    public static readonly IReadOnlyList<PaymentMethodOption> All = new[]
    {
        new PaymentMethodOption(
            Code: "card",
            Title: "Банковская карта",
            Description: "Visa, MasterCard, МИР",
            IconClass: "bi-credit-card",
            Gradient: "linear-gradient(135deg, #667eea, #764ba2)",
            EnumValue: PaymentMethod.Card,
            Available: true),

        new PaymentMethodOption(
            Code: "sbp",
            Title: "СБП",
            Description: "Система быстрых платежей (включая QR-код)",
            IconClass: "bi-lightning-charge",
            Gradient: "linear-gradient(135deg, #8b5cf6, #6d28d9)",
            EnumValue: PaymentMethod.SBP,
            Available: true),

        new PaymentMethodOption(
            Code: "qr",
            Title: "QR-код",
            Description: "Оплата по QR-коду",
            IconClass: "bi-qr-code",
            Gradient: "linear-gradient(135deg, #f59e0b, #d97706)",
            EnumValue: PaymentMethod.SBP,
            Available: false,
            UnavailableHint: "Скоро"),

        new PaymentMethodOption(
            Code: "p2p",
            Title: "P2P перевод",
            Description: "Перевод на карту",
            IconClass: "bi-people",
            Gradient: "linear-gradient(135deg, #ec4899, #e11d48)",
            EnumValue: PaymentMethod.Card,
            Available: false,
            UnavailableHint: "Скоро"),
    };

    public static PaymentMethodOption? Get(string? code) =>
        string.IsNullOrEmpty(code)
            ? null
            : All.FirstOrDefault(m => m.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Map a UI code (`card`, `sbp`, `qr`, `p2p`) to the backend
    /// <see cref="PaymentMethod"/> enum used by the PSP layer.
    /// Falls back to <see cref="PaymentMethod.Card"/> for unknown codes.
    /// </summary>
    public static PaymentMethod ToEnum(string? code) =>
        Get(code)?.EnumValue ?? PaymentMethod.Card;

    /// <summary>True if at least one PSP backs this method right now.</summary>
    public static bool IsAvailable(string? code) =>
        Get(code)?.Available ?? false;
}
