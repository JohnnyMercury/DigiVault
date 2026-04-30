namespace DigiVault.Web.Models.Payment;

/// <summary>
/// Configuration for the shared <c>_PaymentMethodTiles.cshtml</c> partial.
/// Pages just supply how the picker should behave on this particular screen.
/// </summary>
public class PaymentTilesViewModel
{
    /// <summary>Render the «Баланс аккаунта» tile on top.</summary>
    public bool ShowBalance { get; set; }

    /// <summary>Current balance value to display on the balance tile.</summary>
    public decimal Balance { get; set; }

    /// <summary>Code that should be pre-selected (e.g. <c>balance</c>, <c>card</c>).</summary>
    public string SelectedCode { get; set; } = "card";

    /// <summary>
    /// Name of a global JS function called as <c>onclick="<i>fn</i>(this)"</c>.
    /// Default <c>selectPayMethod</c> is provided by the partial itself
    /// - pages can override (e.g. <c>selectModalPayment</c> for the
    /// catalog modals that already have such a function).
    /// </summary>
    public string OnSelectJs { get; set; } = "selectPayMethod";

    /// <summary>
    /// If set, the partial also renders a hidden input with this name and
    /// updates its value in <c>selectPayMethod</c> when the selection changes.
    /// Pages that read the chosen method through pure JS can leave this
    /// <c>null</c>.
    /// </summary>
    public string? HiddenInputName { get; set; }

    /// <summary>id of the rendered hidden input (defaults to <c>selectedPaymentMethod</c>).</summary>
    public string HiddenInputId { get; set; } = "selectedPaymentMethod";
}
