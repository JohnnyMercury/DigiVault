using Microsoft.Extensions.Options;

namespace DigiVault.Web.Services.Payment;

/// <summary>
/// Configuration for <see cref="PaymentAnonymizer"/>. Lives in
/// appsettings.json under the "PaymentAnonymization" section.
///
/// <para>The <see cref="Emails"/> list is a curated set of internal /
/// testing / demo accounts whose payments should appear unique to PSP-side
/// antifraud — same email + same IP + same user-agent on repeat purchases
/// is the #1 fingerprint that gets these accounts auto-blocked.</para>
///
/// <para>Importantly: this list MUST stay small and contain only known-
/// problematic accounts. Anonymising 100% of traffic causes the opposite
/// problem — antifraud sees a sea of "unique-looking new users" and starts
/// blocking everyone.</para>
/// </summary>
public class PaymentAnonymizationOptions
{
    public const string SectionName = "PaymentAnonymization";

    /// <summary>
    /// Email addresses for which outgoing PSP requests get rewritten with
    /// realistic-looking random email/phone/IP. Compared case-insensitively
    /// against <see cref="DigiVault.Core.Models.Payment.PaymentRequest.Email"/>.
    /// </summary>
    public List<string> Emails { get; set; } = new();
}

/// <summary>
/// Result of <see cref="PaymentAnonymizer.Anonymize"/> — same-shape "contacts"
/// as the input PaymentRequest, with anonymisation applied for whitelisted
/// users and pass-through for everyone else.
///
/// <para><see cref="Anonymized"/> is true when at least one field was
/// substituted; providers that have logging or telemetry can use it to
/// decide whether to redact in logs.</para>
/// </summary>
public sealed record AnonymizedContacts(
    string Email,
    string Phone,
    string Ip,
    string Name,
    bool Anonymized);

/// <summary>
/// Generates realistic-looking Russian fake contact data (email / phone / IP)
/// for outbound payment-provider requests. Used by all PSP integrations to
/// keep the antifraud-bypass logic in one place — centralising it means
/// future providers (and the existing Enot/Overpay/PaymentLink set) all
/// get the same treatment without duplicating the generators.
///
/// <para>Behaviour by user:
/// <list type="bullet">
///   <item>If the original email is in <see cref="PaymentAnonymizationOptions.Emails"/>:
///         email + phone + IP are all generated fresh per-call.</item>
///   <item>Otherwise: original email and phone pass through unchanged; IP is
///         the original (typically the customer's real IP from
///         HttpContext.Connection.RemoteIpAddress).</item>
/// </list></para>
/// </summary>
public class PaymentAnonymizer
{
    // ──────────────────────────────────────────────────────────────────
    // Russian-realistic data pools. These were chosen to match what an
    // average ru-locale customer profile looks like to a payment-network
    // antifraud feed.
    // ──────────────────────────────────────────────────────────────────

    private static readonly string[] FirstNames =
    {
        "alexander", "alexey", "andrey", "anton", "artem", "boris", "denis",
        "dmitry", "evgeny", "fedor", "gleb", "igor", "ilya", "ivan", "kirill",
        "konstantin", "leonid", "maxim", "mikhail", "nikita", "nikolay",
        "oleg", "pavel", "pyotr", "roman", "ruslan", "sergey", "stanislav",
        "stepan", "timofey", "vadim", "valery", "viktor", "vitaly",
        "vladimir", "vladislav", "vsevolod", "yaroslav", "yury",
        "anna", "ekaterina", "elena", "irina", "kristina", "lyudmila",
        "maria", "marina", "nadezhda", "natalia", "olga", "polina",
        "svetlana", "tatiana", "valeria", "victoria", "yulia",
    };

    private static readonly string[] LastNames =
    {
        "ivanov", "smirnov", "kuznetsov", "popov", "vasiliev", "petrov",
        "sokolov", "mikhailov", "novikov", "fedorov", "morozov", "volkov",
        "alekseev", "lebedev", "semenov", "egorov", "pavlov", "kozlov",
        "stepanov", "nikolaev", "orlov", "andreev", "makarov", "nikitin",
        "zaharov", "zaitsev", "soloviev", "borisov", "yakovlev", "grigoriev",
        "romanov", "vorobiev", "sergeev", "kuzmin", "frolov", "alexandrov",
        "dmitriev", "korolev", "gusev", "kiselyov", "ilyin", "maximov",
        "polyakov", "sorokin", "vinogradov", "kovalev", "belov", "medvedev",
        "antonov", "tarasov", "zhukov", "baranov", "filippov", "komarov",
        "davydov", "belyaev", "gerasimov", "bogdanov", "osipov", "sidorov",
    };

    private static readonly string[] EmailDomains =
    {
        "yandex.ru", "yandex.ru", "yandex.ru", // weighted higher — most common
        "mail.ru", "mail.ru", "mail.ru",
        "gmail.com", "gmail.com",
        "rambler.ru",
        "list.ru",
        "inbox.ru",
        "bk.ru",
        "yandex.com",
        "internet.ru",
        "icloud.com",
    };

    /// <summary>
    /// Russian mobile operator codes (NDC, 3-digit). Curated subset across
    /// all four majors so the anonymised phones don't all fall into one
    /// operator's range. Code source: Roskomnadzor's allocation table.
    /// </summary>
    private static readonly string[] MobileOperatorCodes =
    {
        // МТС
        "910", "911", "912", "913", "914", "915", "916", "917", "918", "919",
        "980", "981", "982", "983", "984", "985", "986", "987", "988", "989",
        // МегаФон
        "920", "921", "922", "923", "924", "925", "926", "927", "928", "929",
        "930", "931", "932", "933", "934", "936", "937", "938", "999",
        // Билайн
        "903", "905", "906", "909",
        "950", "951", "953",
        "960", "961", "962", "963", "964", "965", "966", "967", "968",
        // Tele2
        "900", "901", "902", "904", "908",
        "952", "977", "991", "992", "993", "994", "995", "996", "997",
    };

    /// <summary>
    /// Russian ISP first-octet pool. Picking from these (instead of a fully
    /// random IPv4) ensures the anonymised IP geolocates to RU/CIS instead
    /// of e.g. an AWS data centre, which would itself be a fraud signal.
    /// Sourced from RIPE's RU/CIS allocations (approximate).
    /// </summary>
    private static readonly byte[] RussianFirstOctets =
    {
        5, 31, 37, 46, 62, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88,
        89, 90, 91, 92, 93, 94, 95, 109, 128, 178, 185, 188, 193, 194, 195,
        212, 213, 217,
    };

    private readonly HashSet<string> _whitelistEmails;

    public PaymentAnonymizer(IOptions<PaymentAnonymizationOptions> options)
    {
        _whitelistEmails = new HashSet<string>(
            options.Value.Emails ?? new List<string>(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True iff the given email is in the anonymisation whitelist.
    /// </summary>
    public bool ShouldAnonymize(string? originalEmail)
        => !string.IsNullOrWhiteSpace(originalEmail)
           && _whitelistEmails.Contains(originalEmail);

    /// <summary>
    /// Builds the email/phone/IP triple for an outbound PSP request. For
    /// whitelisted users every field is freshly random per call. For real
    /// users the originals pass through (with a basic placeholder fallback
    /// if a field is missing — PSPs typically reject empty contact fields).
    /// </summary>
    public AnonymizedContacts Anonymize(
        string? originalEmail,
        string? originalPhone,
        string? originalIp)
    {
        var anonymize = ShouldAnonymize(originalEmail);

        if (anonymize)
        {
            return new AnonymizedContacts(
                Email: GenerateRussianEmail(),
                Phone: GenerateRussianMobilePhone(),
                Ip:    GenerateRussianIp(),
                Name:  GenerateRussianName(),
                Anonymized: true);
        }

        // Pass-through for real users; minimal sanitisation (digits-only
        // phone, fallback placeholders if completely empty so the PSP
        // validator doesn't reject the request).
        var email = !string.IsNullOrWhiteSpace(originalEmail)
            ? originalEmail!
            : GenerateRussianEmail(); // unknown guest → blend in

        var phone = NormalizePhone(originalPhone);
        if (string.IsNullOrEmpty(phone))
            phone = GenerateRussianMobilePhone();

        var ip = !string.IsNullOrWhiteSpace(originalIp)
            ? originalIp!
            : GenerateRussianIp();

        // Real users: we don't have their name in this call, so leave it
        // empty — providers keep their own placeholder (e.g. «Покупатель»).
        return new AnonymizedContacts(email, phone, ip, "", false);
    }

    // ──────────────────────────────────────────────────────────────────
    // Generators
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns something like <c>nikita.kuznetsov12@yandex.ru</c>. The
    /// numeric suffix keeps collisions on common name pairs unlikely.
    /// </summary>
    private static string GenerateRussianEmail()
    {
        var first  = Pick(FirstNames);
        var last   = Pick(LastNames);
        var suffix = Random.Shared.Next(1, 9999);
        var domain = Pick(EmailDomains);
        // Common email styles: name.surname / namesurname / name_surname
        var sep = Random.Shared.Next(3) switch { 0 => ".", 1 => "", _ => "_" };
        return $"{first}{sep}{last}{suffix}@{domain}";
    }

    /// <summary>
    /// Returns 11-digit RU mobile phone, e.g. <c>79161234567</c>. Operator
    /// code is rotated from a curated pool so every payment looks like it
    /// comes from a different SIM.
    /// </summary>
    private static string GenerateRussianMobilePhone()
    {
        var code = Pick(MobileOperatorCodes);
        var line = Random.Shared.Next(1_000_000, 10_000_000); // 7 digits
        return $"7{code}{line:D7}";
    }

    /// <summary>
    /// Returns a publicly routable IPv4 string from the RU/CIS first-octet
    /// pool. We avoid private (RFC1918) and reserved ranges automatically
    /// because the pool only contains public allocations.
    /// </summary>
    private static string GenerateRussianIp()
    {
        var a = Pick(RussianFirstOctets);
        var b = Random.Shared.Next(0, 256);
        var c = Random.Shared.Next(0, 256);
        // Skip .0 and .255 — broadcasts/network addresses look unnatural.
        var d = Random.Shared.Next(1, 255);
        return $"{a}.{b}.{c}.{d}";
    }

    // Cyrillic name pools for the PSP name field. Russian PSPs see real
    // customers type Cyrillic names, so this blends in better than translit.
    // Surnames are stored in masculine base form; the feminine variant just
    // appends «а» (all entries are -ов/-ев/-ин which agree this way).
    private static readonly string[] MaleFirstNamesRu =
    {
        "Александр", "Алексей", "Андрей", "Антон", "Артём", "Борис", "Денис",
        "Дмитрий", "Евгений", "Иван", "Игорь", "Илья", "Кирилл", "Константин",
        "Максим", "Михаил", "Никита", "Николай", "Олег", "Павел", "Роман",
        "Руслан", "Сергей", "Степан", "Тимофей", "Вадим", "Виктор", "Владимир",
        "Владислав", "Ярослав", "Юрий",
    };

    private static readonly string[] FemaleFirstNamesRu =
    {
        "Анна", "Екатерина", "Елена", "Ирина", "Кристина", "Людмила", "Мария",
        "Марина", "Надежда", "Наталья", "Ольга", "Полина", "Светлана",
        "Татьяна", "Валерия", "Виктория", "Юлия", "Дарья", "Ксения", "Алина",
    };

    private static readonly string[] LastNamesRu =
    {
        "Иванов", "Смирнов", "Кузнецов", "Попов", "Васильев", "Петров",
        "Соколов", "Михайлов", "Новиков", "Фёдоров", "Морозов", "Волков",
        "Алексеев", "Лебедев", "Семёнов", "Егоров", "Павлов", "Козлов",
        "Степанов", "Николаев", "Орлов", "Андреев", "Макаров", "Никитин",
        "Захаров", "Зайцев", "Соловьёв", "Борисов", "Яковлев", "Григорьев",
        "Романов", "Воробьёв", "Сергеев", "Кузьмин", "Фролов", "Максимов",
        "Поляков", "Сорокин", "Виноградов", "Ковалёв", "Белов", "Медведев",
        "Антонов", "Тарасов", "Жуков", "Баранов", "Филиппов", "Комаров",
    };

    /// <summary>
    /// Returns a realistic Cyrillic RU full name with gender agreement, e.g.
    /// <c>Дмитрий Иванов</c> or <c>Анна Иванова</c>. Fresh per call.
    /// </summary>
    private static string GenerateRussianName()
    {
        var male = Random.Shared.Next(2) == 0;
        var first = Pick(male ? MaleFirstNamesRu : FemaleFirstNamesRu);
        var last  = Pick(LastNamesRu) + (male ? "" : "а");
        return $"{first} {last}";
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    private static string NormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits.StartsWith("8"))
            digits = "7" + digits.Substring(1);
        return digits;
    }

    private static T Pick<T>(IReadOnlyList<T> pool) =>
        pool[Random.Shared.Next(pool.Count)];
}
