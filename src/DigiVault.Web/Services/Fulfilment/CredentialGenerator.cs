using DigiVault.Core.Entities;
using Microsoft.Extensions.Configuration;

namespace DigiVault.Web.Services.Fulfilment;

/// <summary>
/// Produces a <see cref="DeliveryPayload"/> for a single purchased
/// <see cref="OrderItem"/>. Two delivery modes:
///   - <see cref="CodeCredential"/>: realistic-looking activation code
///     (Steam-style game keys, gift-card codes for PSN/Xbox/Nintendo/Apple,
///     subscription codes). Customer redeems it on the issuer's platform.
///   - <see cref="ContactSupportCredential"/>: «security check» banner with a
///     Telegram contact CTA. Used wherever the product needs an operator
///     in the loop - Steam Wallet top-ups, in-game currency (operator must
///     enter the player ID), VPN account access, Telegram Premium activation.
///
/// To change the policy for a category, edit the dispatch in
/// <see cref="Generate"/>. Routing is intentionally explicit so a glance at
/// this file tells you exactly what every product type does after payment.
/// </summary>
public interface ICredentialGenerator
{
    DeliveryPayload Generate(OrderItem item);
}

public class CredentialGenerator : ICredentialGenerator
{
    // Single Random instance is fine - fulfilment runs serialised in BG service
    // and order purchases are infrequent. Lock not needed.
    private readonly Random _rnd = new();
    private readonly string _supportUsername;

    public CredentialGenerator(IConfiguration cfg)
    {
        // Single source of truth for the Telegram support handle. Change in
        // appsettings.json (Support:TelegramUsername) - every new payload
        // picks it up immediately. Stored on each payload as a snapshot, so
        // historical orders keep showing the contact they were issued with.
        _supportUsername = (cfg["Support:TelegramUsername"] ?? "digivault_support")
            .TrimStart('@')
            .Trim();
    }

    public DeliveryPayload Generate(OrderItem item)
    {
        var p = item.GameProduct
            ?? throw new InvalidOperationException("OrderItem.GameProduct must be eager-loaded before fulfilment.");

        // ── Operator-handled flows ────────────────────────────────────────
        // In-game currency: operator must enter the player ID after payment.
        if (p.Game != null)
            return BuildGameCurrencySupport(p, item);

        // Telegram Premium: activation requires Telegram-side action.
        if (p.GiftCard != null && p.GiftCard.Slug == "telegram-premium")
            return BuildTelegramPremiumSupport(p, item);

        // VPN providers: subscription credentials are tied to operator's pool
        // accounts - handed out by support after a quick verification.
        if (p.VpnProvider != null)
            return BuildVpnSupport(p, item);

        // Steam Wallet top-up: payment goes on a security-check hold.
        if (p.GiftCard != null && p.GiftCard.Slug == "steam-wallet")
            return BuildSteamWalletSupport(p, item);

        // ── Auto-issued codes ─────────────────────────────────────────────
        // Other gift cards (PSN / Xbox / Nintendo / Apple / Spotify / …):
        // realistic-looking code generated on the fly.
        if (p.GiftCard != null)
            return BuildGiftCardCode(p);

        // Fallback - opaque code with a support pointer.
        return new CodeCredential
        {
            Code = RandomCode("KEY", 4, 4),
            Instructions = $"Свяжитесь с поддержкой @{_supportUsername} для активации.",
        };
    }

    // ────────────────────────────────────────────────────────────────────
    // Operator-handled (ContactSupportCredential)
    // ────────────────────────────────────────────────────────────────────

    private ContactSupportCredential BuildGameCurrencySupport(GameProduct p, OrderItem item)
    {
        var recipient = ResolveRecipient(item);
        var product   = !string.IsNullOrWhiteSpace(p.TotalDisplay) ? p.TotalDisplay! : p.Name;
        return new ContactSupportCredential
        {
            Title = "Заказ ожидает зачисления",
            Message =
                "Платёж получен. Чтобы зачислить игровую валюту на ваш аккаунт, " +
                "оператору нужен ваш игровой ID. Напишите нам в Telegram - закроем заказ за пару минут.",
            SupportUsername = _supportUsername,
            OrderRef        = OrderRef(item),
            CompletedAt     = item.DeliveredAt ?? DateTime.UtcNow,
            Recipient       = recipient,
            ProductName     = product,
        };
    }

    private ContactSupportCredential BuildTelegramPremiumSupport(GameProduct p, OrderItem item)
    {
        var recipient = ResolveRecipient(item);
        if (!string.IsNullOrEmpty(recipient) && !recipient.StartsWith("@"))
            recipient = "@" + recipient.TrimStart('@');

        var months = 3;
        if (!string.IsNullOrEmpty(p.Amount))
        {
            var digits = new string(p.Amount.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var m) && m is >= 1 and <= 24) months = m;
        }

        return new ContactSupportCredential
        {
            Title = "Активация Telegram Premium",
            Message =
                "Платёж получен. Для активации Premium на вашем аккаунте напишите нам в Telegram - " +
                "оператор подтвердит ник и активирует подписку.",
            SupportUsername = _supportUsername,
            OrderRef        = OrderRef(item),
            CompletedAt     = item.DeliveredAt ?? DateTime.UtcNow,
            Recipient       = recipient,
            ProductName     = !string.IsNullOrWhiteSpace(p.TotalDisplay)
                ? p.TotalDisplay!
                : $"Telegram Premium, {months} мес.",
        };
    }

    private ContactSupportCredential BuildVpnSupport(GameProduct p, OrderItem item)
    {
        var slug    = p.VpnProvider!.Slug;
        var name    = p.VpnProvider.Name ?? slug;
        var product = !string.IsNullOrWhiteSpace(p.TotalDisplay) ? p.TotalDisplay! : $"{name} - подписка";

        return new ContactSupportCredential
        {
            Title = "Выдача доступа к VPN",
            Message =
                $"Платёж получен. {name} выдаём вручную из проверенного пула аккаунтов - " +
                "напишите нам в Telegram, оператор пришлёт логин, пароль и инструкцию по входу.",
            SupportUsername = _supportUsername,
            OrderRef        = OrderRef(item),
            CompletedAt     = item.DeliveredAt ?? DateTime.UtcNow,
            Recipient       = ResolveRecipient(item),
            ProductName     = product,
        };
    }

    private ContactSupportCredential BuildSteamWalletSupport(GameProduct p, OrderItem item)
    {
        var product = !string.IsNullOrWhiteSpace(p.TotalDisplay) ? p.TotalDisplay! : p.Name;
        return new ContactSupportCredential
        {
            Title = "Платёж попал на проверку Системой безопасности",
            Message =
                "Steam усилил защиту аккаунтов от автоматических пополнений - поэтому каждое " +
                "пополнение мы зачисляем после короткой ручной проверки. Напишите нам в Telegram " +
                "- оператор подтвердит ваш Steam-логин и начислит средства за 5-10 минут.",
            SupportUsername = _supportUsername,
            OrderRef        = OrderRef(item),
            CompletedAt     = item.DeliveredAt ?? DateTime.UtcNow,
            Recipient       = ResolveRecipient(item),
            ProductName     = product,
        };
    }

    // ────────────────────────────────────────────────────────────────────
    // Gift cards
    // ────────────────────────────────────────────────────────────────────

    private DeliveryPayload BuildGiftCardCode(GameProduct p)
    {
        var slug = p.GiftCard!.Slug;
        var region = p.Region; // e.g. "USA", "RU", "TR"

        return slug switch
        {
            "psn"          => new CodeCredential { Code = Group(_rnd, 4, 4, 4), Region = region ?? "USA",
                                                   Instructions = "Активируйте код в PlayStation Store: Settings → Redeem Code." },
            "xbox"         => new CodeCredential { Code = Group(_rnd, 5, 5, 5, 5, 5), Region = region ?? "USA",
                                                   Instructions = "Активируйте код в Xbox Store или на redeem.microsoft.com." },
            "nintendo"     => new CodeCredential { Code = Group(_rnd, 4, 4, 4, 4), Region = region ?? "USA",
                                                   Instructions = "Активируйте на Nintendo eShop: Меню → Ввести код активации." },
            "stream-cards" => new CodeCredential { Code = "NFLX-" + Group(_rnd, 5, 5, 5, 5),
                                                   Instructions = "Введите код на netflix.com/redeem." },
            "spotify"      => new CodeCredential { Code = "SPTF-" + Group(_rnd, 4, 4, 4),
                                                   Instructions = "Введите код на spotify.com/redeem." },
            "apple"        => new CodeCredential { Code = AppleStyle(), Region = region ?? "USA",
                                                   Instructions = "Откройте App Store → нажмите аватар → «Использовать подарочную карту»." },
            "youtube"      => new CodeCredential { Code = "YT-" + Group(_rnd, 4, 4, 4, 4),
                                                   Instructions = "Введите код на youtube.com/redeem." },
            _              => new CodeCredential { Code = Group(_rnd, 4, 4, 4, 4), Region = region,
                                                   Instructions = "Активируйте код на сайте сервиса." },
        };
    }

    // ────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────

    /// <summary>Resolves the recipient from Order.DeliveryInfo (set at purchase time).</summary>
    private static string? ResolveRecipient(OrderItem item)
    {
        var info = item.Order?.DeliveryInfo;
        return string.IsNullOrWhiteSpace(info) ? null : info.Trim();
    }

    /// <summary>Order reference shown to the operator (Order number + item id).</summary>
    private static string OrderRef(OrderItem item)
    {
        var num = item.Order?.OrderNumber ?? $"DV-{item.OrderId}";
        return $"{num} / item {item.Id}";
    }

    /// <summary>Generates an opaque-looking code prefix-XXXX-XXXXXX (custom group sizes).</summary>
    private string RandomCode(string prefix, params int[] groupSizes) =>
        prefix + "-" + Group(_rnd, groupSizes);

    /// <summary>Joins groups of random alphanumerics with dashes: "ABCD-EF12-XYZ9".</summary>
    private static string Group(Random rnd, params int[] groupSizes)
    {
        const string alpha = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no I/O/0/1 - anti-confusion
        return string.Join("-",
            groupSizes.Select(size =>
                new string(Enumerable.Range(0, size).Select(_ => alpha[rnd.Next(alpha.Length)]).ToArray())));
    }

    /// <summary>Apple-style 16-char code: XX-XX-XXXXXXX-XXXXX (mostly alphanumeric).</summary>
    private string AppleStyle()
    {
        const string alpha = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        string Pick(int n) => new(Enumerable.Range(0, n).Select(_ => alpha[_rnd.Next(alpha.Length)]).ToArray());
        return $"{Pick(2)}-{Pick(2)}-{Pick(7)}-{Pick(5)}";
    }
}
