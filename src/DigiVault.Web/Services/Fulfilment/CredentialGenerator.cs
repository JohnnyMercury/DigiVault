using DigiVault.Core.Entities;

namespace DigiVault.Web.Services.Fulfilment;

/// <summary>
/// Produces a realistic-looking <see cref="DeliveryPayload"/> for a single
/// purchased <see cref="OrderItem"/>. Picks the variant (code vs confirmation)
/// based on the parent product type:
///   - Game (currency) / Telegram Premium → ConfirmationCredential
///   - GiftCard / VpnProvider             → CodeCredential
/// </summary>
public interface ICredentialGenerator
{
    DeliveryPayload Generate(OrderItem item);
}

public class CredentialGenerator : ICredentialGenerator
{
    // Single Random instance is fine — fulfilment runs serialised in BG service
    // and order purchases are infrequent. Lock not needed.
    private readonly Random _rnd = new();

    public DeliveryPayload Generate(OrderItem item)
    {
        var p = item.GameProduct
            ?? throw new InvalidOperationException("OrderItem.GameProduct must be eager-loaded before fulfilment.");

        // 1. In-game currency / Telegram Premium → confirmation receipt
        if (p.Game != null)
            return BuildGameCurrencyConfirmation(p, item);

        if (p.GiftCard != null && p.GiftCard.Slug == "telegram-premium")
            return BuildTelegramPremiumConfirmation(p, item);

        // 2. Gift cards → activation code
        if (p.GiftCard != null)
            return BuildGiftCardCode(p);

        // 3. VPN providers → activation code
        if (p.VpnProvider != null)
            return BuildVpnCode(p);

        // 4. Fallback — opaque code
        return new CodeCredential
        {
            Code = RandomCode("KEY", 4, 4),
            Instructions = "Свяжитесь с поддержкой для активации.",
        };
    }

    // ────────────────────────────────────────────────────────────────────
    // Game currency
    // ────────────────────────────────────────────────────────────────────

    private DeliveryPayload BuildGameCurrencyConfirmation(GameProduct p, OrderItem item)
    {
        var recipient = ResolveRecipient(item) ?? "ваш аккаунт";
        return new ConfirmationCredential
        {
            Recipient = recipient,
            Amount = !string.IsNullOrWhiteSpace(p.TotalDisplay) ? p.TotalDisplay! : p.Name,
            TransactionId = RandomCode("TXN", 4, 6),
            CompletedAt = item.DeliveredAt ?? DateTime.UtcNow,
        };
    }

    private DeliveryPayload BuildTelegramPremiumConfirmation(GameProduct p, OrderItem item)
    {
        var recipient = ResolveRecipient(item) ?? "@username";
        if (!recipient.StartsWith("@")) recipient = "@" + recipient.TrimStart('@');

        // Try to infer subscription length from Amount ("3 мес", "6 мес", "12 мес")
        var months = 3;
        if (!string.IsNullOrEmpty(p.Amount))
        {
            var digits = new string(p.Amount.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var m) && m is >= 1 and <= 24) months = m;
        }
        var completed = item.DeliveredAt ?? DateTime.UtcNow;

        return new ConfirmationCredential
        {
            Recipient = recipient,
            Amount = !string.IsNullOrWhiteSpace(p.TotalDisplay) ? p.TotalDisplay! : $"Telegram Premium, {months} мес.",
            TransactionId = RandomCode("TGP", 4, 6),
            CompletedAt = completed,
            ValidUntil = completed.AddMonths(months).ToString("yyyy-MM-dd"),
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
    // VPN providers
    // ────────────────────────────────────────────────────────────────────

    private DeliveryPayload BuildVpnCode(GameProduct p)
    {
        var slug = p.VpnProvider!.Slug;
        var prefix = slug switch
        {
            "nordvpn"    => "NORD",
            "expressvpn" => "EXPR",
            "surfshark"  => "SURF",
            _ => "VPN",
        };
        var domain = slug switch
        {
            "nordvpn"    => "nordvpn.com",
            "expressvpn" => "expressvpn.com",
            "surfshark"  => "surfshark.com",
            _ => "vpn.com",
        };

        return new CodeCredential
        {
            Code = prefix + "-" + Group(_rnd, 4, 4, 4, 4, 4),
            Instructions = $"Войдите в свой аккаунт на {domain} → раздел «Активация» → введите код.",
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

    /// <summary>Generates an opaque-looking code prefix-XXXX-XXXXXX (custom group sizes).</summary>
    private string RandomCode(string prefix, params int[] groupSizes) =>
        prefix + "-" + Group(_rnd, groupSizes);

    /// <summary>Joins groups of random alphanumerics with dashes: "ABCD-EF12-XYZ9".</summary>
    private static string Group(Random rnd, params int[] groupSizes)
    {
        const string alpha = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no I/O/0/1 — anti-confusion
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
