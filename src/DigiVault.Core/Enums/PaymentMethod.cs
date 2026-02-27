namespace DigiVault.Core.Enums;

/// <summary>
/// Методы оплаты
/// </summary>
public enum PaymentMethod
{
    /// <summary>Банковская карта (Visa, MasterCard, МИР)</summary>
    Card = 0,

    /// <summary>Система быстрых платежей</summary>
    SBP = 1,

    /// <summary>SberPay</summary>
    SberPay = 2,

    /// <summary>ЮMoney (Яндекс.Деньги)</summary>
    YooMoney = 3,

    /// <summary>QIWI Кошелек</summary>
    Qiwi = 4,

    /// <summary>WebMoney</summary>
    WebMoney = 5,

    /// <summary>Криптовалюта</summary>
    Crypto = 6,

    /// <summary>PayPal</summary>
    PayPal = 7,

    /// <summary>Баланс аккаунта</summary>
    Balance = 8,

    /// <summary>Мобильный платеж</summary>
    Mobile = 9
}
