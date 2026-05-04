namespace DigiVault.Web.Services.Payment;

/// <summary>
/// Builds opaque-looking merchantTransactionId values for outbound PSP calls.
///
/// Why: payment-network antifraud and competing aggregators fingerprint
/// merchants by the prefix they put on transaction identifiers (e.g. a fixed
/// "kz-…" or "TRX…" prefix on every txn → easy to cluster all our payments
/// together). Rotating a small per-call prefix from a curated pool breaks the
/// pattern without making the id unguessable for legitimate reconciliation.
///
/// The pool intentionally excludes:
///   • payment-related abbreviations: tx, tr, trx, pm, pp, pl, pg, ps;
///   • order-related abbreviations: id, or, ol, on;
///   • brand initials we actively avoid: kz, dv, nh, ng, cb, cy.
///
/// 22 codes × random hex tail = enough variation that every batch of N
/// payments has a different leading-letter histogram than a fixed-prefix
/// shop, which is what fingerprinting relies on.
/// </summary>
internal static class TxnIdHelper
{
    private static readonly string[] PrefixPool =
    {
        "aw", "bx", "cm", "dh", "eq", "fk", "gn", "hv",
        "jt", "kr", "ld", "mp", "nb", "oz", "pf", "qy",
        "rs", "ug", "vw", "xk", "yh", "zl",
    };

    /// <summary>
    /// 2-letter random prefix from the pool. Cheap call, thread-safe via
    /// Random.Shared (singleton, Seed is per-thread internally).
    /// </summary>
    public static string RandomPrefix() =>
        PrefixPool[Random.Shared.Next(PrefixPool.Length)];

    /// <summary>
    /// Generate a transaction id capped at <paramref name="maxLength"/>
    /// characters total, using a 2-letter rotating prefix and a random hex
    /// tail. Guaranteed alphanumeric (a-z, 0-9) — fits PaymentLink's
    /// `number` regex (0-9 a-z A-Z) and Overpay's free-form id field.
    /// </summary>
    public static string Generate(int maxLength = 28)
    {
        if (maxLength < 6) maxLength = 6;
        var prefix = RandomPrefix();
        var tailLen = Math.Min(32, maxLength - prefix.Length);
        var tail = Guid.NewGuid().ToString("N").Substring(0, tailLen);
        return prefix + tail;
    }
}
