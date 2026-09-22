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
            // One coherent identity: the email is transliterated from the
            // same name, so «Дмитрий Иванов» pairs with dmitriy.ivanov@… .
            var (genName, genEmail) = GenerateRussianIdentity();
            return new AnonymizedContacts(
                Email: genEmail,
                Phone: GenerateRussianMobilePhone(),
                Ip:    GenerateRussianIp(),
                Name:  genName,
                Anonymized: true);
        }

        // Pass-through for real users; minimal sanitisation (digits-only
        // phone, fallback placeholders if completely empty so the PSP
        // validator doesn't reject the request).
        var email = !string.IsNullOrWhiteSpace(originalEmail)
            ? originalEmail!
            : GenerateDiverseEmail(); // unknown guest → blend in

        var phone = NormalizePhone(originalPhone);
        if (string.IsNullOrEmpty(phone))
            phone = GenerateDeterministicPhone(email);

        var ip = !string.IsNullOrWhiteSpace(originalIp)
            ? originalIp!
            : GenerateRussianIp();

        // Real users: we don't have their name in this call, so leave it
        // empty — providers keep their own placeholder (e.g. «Покупатель»).
        return new AnonymizedContacts(email, phone, ip, "", false);
    }

    /// <summary>
    /// The user-identifier to send to a PSP — ParityPay calls this field
    /// <c>user_hash</c>; other providers use <c>client_id</c>/<c>user_id</c>/
    /// <c>merchantUserID</c>. For whitelisted internal-test accounts a fresh
    /// random id is returned per call, so repeat purchases from the same
    /// account don't share a constant user id (that constant is a textbook
    /// antifraud fingerprint). Real users get their actual
    /// <paramref name="originalUserId"/> unchanged.
    ///
    /// <para>Note: this only affects the value sent OUTBOUND to the PSP. Our
    /// own crediting and audit key on the stored
    /// <see cref="DigiVault.Core.Entities.PaymentTransaction.UserId"/> and the
    /// internal transaction id, never on this value — so randomising it here is
    /// safe.</para>
    /// </summary>
    public string AnonymizeUserId(string? originalEmail, string originalUserId)
        => ShouldAnonymize(originalEmail)
            ? GenerateUserHash()
            : originalUserId;

    /// <summary>
    /// Returns a display-safe user id for admin views. Whitelisted accounts
    /// get a deterministic fake GUID (same seed → same result every time),
    /// real users get their original id unchanged.
    /// </summary>
    public string DisplayUserId(string? originalEmail, string originalUserId, int seed)
    {
        if (!ShouldAnonymize(originalEmail))
            return originalUserId;
        var rnd = new Random(seed);
        var bytes = new byte[16];
        rnd.NextBytes(bytes);
        return new Guid(bytes).ToString();
    }

    /// <summary>
    /// Returns a display-safe email for admin views. Whitelisted accounts
    /// get a deterministic fake email (same seed → same result every time),
    /// real users get their original email unchanged.
    /// </summary>
    public string DisplayEmail(string? originalEmail, int seed)
    {
        if (!ShouldAnonymize(originalEmail))
            return originalEmail ?? "";
        var rnd = new Random(seed);
        var domain = DisplayDomains[rnd.Next(DisplayDomains.Length)];
        var nick = Nicknames[rnd.Next(Nicknames.Length)];
        var nick2 = Nicknames[rnd.Next(Nicknames.Length)];
        var adj = Adjectives[rnd.Next(Adjectives.Length)];
        var noun = Nouns[rnd.Next(Nouns.Length)];
        var noun2 = Nouns[rnd.Next(Nouns.Length)];
        var ruFirst = FirstNames[rnd.Next(FirstNames.Length)];
        var ruLast = LastNames[rnd.Next(LastNames.Length)];
        var yr = rnd.Next(1985, 2006);
        var n2 = rnd.Next(1, 999);
        var n3 = rnd.Next(1, 9999);
        var sep = rnd.Next(3) switch { 0 => ".", 1 => "_", _ => "" };
        var style = rnd.Next(20);
        var local = style switch
        {
            // ник + число: phantom42, glitch777
            0  => $"{nick}{rnd.Next(1, 99)}",
            // ник + прилагательное: ghost.calm, nova_epic
            1  => $"{nick}{sep}{adj}",
            // прилагательное + существительное + число: dark_wolf795
            2  => $"{adj}{sep}{noun}{n2}",
            // просто ник: maverick, vortex
            3  => $"{nick}",
            // существительное + число: phoenix6101
            4  => $"{noun}{n3}",
            // ник + существительное: frost.blade, neon_hawk
            5  => $"{nick}{sep}{noun}",
            // ник + ник: pixel.storm, cyber_zen
            6  => $"{nick}{sep}{nick2}",
            // xX_ник_Xx / x_ник_x
            7  => rnd.Next(2) == 0 ? $"xX_{nick}_Xx" : $"x_{nick}{rnd.Next(1, 99)}_x",
            // pr0_ник, n1ce_ник
            8  => $"{LeetPrefixes[rnd.Next(LeetPrefixes.Length)]}{nick}",
            // 2fast4u стиль: число + прилагательное + число + существительное
            9  => $"{rnd.Next(2, 10)}{adj}{rnd.Next(2, 10)}{noun}",
            // прилагательное + ник: epic_phantom, wild.blaze
            10 => $"{adj}{sep}{nick}",
            // ник + год: shadow98, nova2001
            11 => $"{nick}{yr % 100:D2}",
            // the_ник, mr_ник, just_ник
            12 => $"{Prefixes[rnd.Next(Prefixes.Length)]}{nick}",
            // существительное + существительное: wolffire, ice_storm
            13 => $"{noun}{sep}{noun2}",
            // ник + _official / _real
            14 => $"{nick}{Suffixes[rnd.Next(Suffixes.Length)]}",
            // рус.имя(транслит) + число: sergey88, ivan_92
            15 => $"{ruFirst}{sep}{rnd.Next(70, 105)}",
            // инициал.рус.фамилия: s.ivanov
            16 => $"{ruFirst[0]}.{ruLast}",
            // прилагательное + число: lucky777, epic42
            17 => $"{adj}{rnd.Next(1, 999)}",
            // not_ / un_ + ник: not_a_ghost, un_rebel
            18 => $"{NegPrefixes[rnd.Next(NegPrefixes.Length)]}{nick}{(rnd.Next(2) == 0 ? rnd.Next(1, 99).ToString() : "")}",
            // ник + прилагательное + число: frost.dark13
            _  => $"{nick}{sep}{adj}{rnd.Next(1, 99)}",
        };
        return $"{local}@{domain}";
    }

    private static readonly string[] DisplayDomains =
    {
        "gmail.com", "gmail.com", "gmail.com", "gmail.com",
        "yahoo.com", "yahoo.com",
        "outlook.com", "hotmail.com",
        "mail.ru", "mail.ru", "mail.ru",
        "yandex.ru", "yandex.ru",
        "icloud.com", "protonmail.com",
        "bk.ru", "inbox.ru", "list.ru",
        "rambler.ru", "live.com",
    };

    private static readonly string[] Nicknames =
    {
        "shadow", "phantom", "ghost", "blaze", "frost", "storm",
        "pixel", "cyber", "neo", "flux", "nova", "zen", "vortex",
        "raven", "echo", "orbit", "drift", "pulse", "crypt", "glitch",
        "jade", "onyx", "ruby", "opal", "ivory", "coral", "amber",
        "maverick", "nomad", "rebel", "strix", "apex", "lynx",
        "cobalt", "neon", "titan", "atlas", "spark", "dash", "bolt",
        "chrome", "indie", "retro", "sonic", "turbo", "rocket",
        "sunny", "cloudy", "rainy", "snowy", "windy", "misty",
        "sleepy", "grumpy", "lucky", "happy", "zippy", "fuzzy",
        "enigma", "cipher", "vector", "matrix", "prism", "helix",
        "wraith", "specter", "mirage", "void", "nebula", "quasar",
        "oxide", "carbon", "argon", "helium", "plasma", "neutron",
        "cactus", "bamboo", "maple", "cedar", "willow", "moss",
        "panda", "otter", "koala", "ferret", "gecko", "parrot",
        "waffle", "pretzel", "donut", "pickle", "mango", "kiwi",
        "disco", "jazz", "tempo", "bass", "synth", "vinyl",
        "dusk", "dawn", "haze", "ember", "ash", "flint",
        "riddle", "puzzle", "trick", "quest", "myth", "saga",
        "ninja", "samurai", "ronin", "viking", "pirate", "wizard",
    };

    private static readonly string[] Adjectives =
    {
        "dark", "lucky", "cool", "crazy", "sweet", "happy", "super",
        "best", "good", "real", "big", "red", "fast", "gold", "nice",
        "pro", "top", "hot", "cold", "wild", "free", "true", "brave",
        "calm", "bright", "magic", "epic", "fresh", "silent", "vivid",
        "royal", "solar", "polar", "urban", "rustic", "noble", "rapid",
        "tiny", "loud", "odd", "rare", "raw", "sly", "lazy", "keen",
        "bold", "grim", "pale", "vast", "wise", "lost", "mad", "lone",
    };

    private static readonly string[] Nouns =
    {
        "angel", "star", "wolf", "fox", "cat", "lion", "tiger",
        "bear", "eagle", "hawk", "fire", "ice", "sun", "moon",
        "sky", "storm", "king", "knight", "shadow", "dream",
        "heart", "soul", "wind", "flame", "rider", "hunter",
        "river", "ocean", "forest", "thunder", "crystal", "arrow",
        "blade", "spark", "phoenix", "dragon", "panther", "falcon",
        "comet", "laser", "garden", "island", "bridge", "tower",
        "pixel", "byte", "logic", "code", "node", "loop",
        "wave", "reef", "dune", "cliff", "peak", "cave",
        "orbit", "lens", "gear", "bolt", "wire", "chip",
    };

    private static readonly string[] Prefixes =
    {
        "the_", "mr_", "pro_", "cool_", "just_", "hey_", "real_",
        "im_", "my_", "dj_", "mc_", "lil_", "big_", "sir_",
    };

    private static readonly string[] LeetPrefixes =
    {
        "pr0_", "n1ce_", "l33t_", "h4x_", "z3r0_", "k1ng_",
        "b0ss_", "w1ld_", "r4w_", "d4rk_", "f1re_", "1ce_",
    };

    private static readonly string[] Suffixes =
    {
        "_official", "_real", "_og", "_hq", "_x", "_v2",
        "_pro", "_fx", "_io", "_gg", "_tv", "_dev",
    };

    private static readonly string[] NegPrefixes =
    {
        "not_a_", "un_", "anti_", "non_", "no_", "zero_",
    };

    // ──────────────────────────────────────────────────────────────────
    // Generators
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// A fresh, realistic-looking user id. Uses the same GUID shape as our
    /// ASP.NET Identity user ids (lowercase, hyphenated), so it's
    /// indistinguishable from a real id to a PSP's antifraud.
    /// </summary>
    private static string GenerateUserHash() => Guid.NewGuid().ToString();

    /// <summary>
    /// Diverse, realistic-looking email. Uses the same 20-style pool as
    /// <see cref="DisplayEmail"/> but with <see cref="Random.Shared"/>
    /// (non-deterministic) so every PSP call gets a unique address whose
    /// format varies — nick-based, adjective+noun, leet, xX_Xx, etc.
    /// </summary>
    private static string GenerateDiverseEmail()
    {
        var rnd    = Random.Shared;
        var domain = Pick(DisplayDomains);
        var nick   = Pick(Nicknames);
        var nick2  = Pick(Nicknames);
        var adj    = Pick(Adjectives);
        var noun   = Pick(Nouns);
        var noun2  = Pick(Nouns);
        var ruFirst = Pick(FirstNames);
        var ruLast  = Pick(LastNames);
        var yr     = rnd.Next(1985, 2006);
        var n2     = rnd.Next(1, 999);
        var n3     = rnd.Next(1, 9999);
        var sep    = rnd.Next(3) switch { 0 => ".", 1 => "_", _ => "" };
        var style  = rnd.Next(20);
        var local = style switch
        {
            0  => $"{nick}{rnd.Next(1, 99)}",
            1  => $"{nick}{sep}{adj}",
            2  => $"{adj}{sep}{noun}{n2}",
            3  => $"{nick}",
            4  => $"{noun}{n3}",
            5  => $"{nick}{sep}{noun}",
            6  => $"{nick}{sep}{nick2}",
            7  => rnd.Next(2) == 0 ? $"xX_{nick}_Xx" : $"x_{nick}{rnd.Next(1, 99)}_x",
            8  => $"{Pick(LeetPrefixes)}{nick}",
            9  => $"{rnd.Next(2, 10)}{adj}{rnd.Next(2, 10)}{noun}",
            10 => $"{adj}{sep}{nick}",
            11 => $"{nick}{yr % 100:D2}",
            12 => $"{Pick(Prefixes)}{nick}",
            13 => $"{noun}{sep}{noun2}",
            14 => $"{nick}{Pick(Suffixes)}",
            15 => $"{ruFirst}{sep}{rnd.Next(70, 105)}",
            16 => $"{ruFirst[0]}.{ruLast}",
            17 => $"{adj}{rnd.Next(1, 999)}",
            18 => $"{Pick(NegPrefixes)}{nick}{(rnd.Next(2) == 0 ? rnd.Next(1, 99).ToString() : "")}",
            _  => $"{nick}{sep}{adj}{rnd.Next(1, 99)}",
        };
        return $"{local}@{domain}";
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

    private static string GenerateDeterministicPhone(string seed)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(seed.ToLowerInvariant()));
        var hash = BitConverter.ToInt32(bytes, 0);
        var rnd = new Random(hash);
        var code = MobileOperatorCodes[rnd.Next(MobileOperatorCodes.Length)];
        var line = rnd.Next(1_000_000, 10_000_000);
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
        "Александр", "Алексей", "Анатолий", "Андрей", "Антон", "Аркадий",
        "Арсений", "Артём", "Артур", "Богдан", "Борис", "Вадим", "Валерий",
        "Василий", "Виктор", "Виталий", "Владимир", "Владислав", "Всеволод",
        "Вячеслав", "Геннадий", "Георгий", "Герман", "Глеб", "Григорий",
        "Даниил", "Демьян", "Денис", "Дмитрий", "Евгений", "Егор", "Захар",
        "Иван", "Игорь", "Илья", "Кирилл", "Константин", "Лев", "Леонид",
        "Макар", "Максим", "Марк", "Матвей", "Михаил", "Назар", "Никита",
        "Николай", "Олег", "Павел", "Платон", "Прохор", "Родион", "Роман",
        "Руслан", "Савелий", "Святослав", "Семён", "Сергей", "Степан",
        "Тимофей", "Тимур", "Фёдор", "Эдуард", "Юрий", "Ярослав",
    };

    private static readonly string[] FemaleFirstNamesRu =
    {
        "Алёна", "Алина", "Алиса", "Анастасия", "Анна", "Валентина", "Валерия",
        "Вера", "Вероника", "Виктория", "Виолетта", "Галина", "Дарина", "Дарья",
        "Диана", "Евгения", "Екатерина", "Елена", "Жанна", "Зоя", "Инна",
        "Ирина", "Карина", "Кристина", "Ксения", "Лариса", "Лидия", "Любовь",
        "Людмила", "Маргарита", "Марина", "Мария", "Милана", "Надежда",
        "Наталья", "Нина", "Оксана", "Ольга", "Полина", "Раиса", "Регина",
        "Светлана", "София", "Тамара", "Татьяна", "Ульяна", "Элина", "Юлия",
        "Яна",
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
        "Давыдов", "Беляев", "Герасимов", "Богданов", "Осипов", "Сидоров",
        "Кудрявцев", "Лукин", "Журавлёв", "Мельников", "Щербаков",
        "Колесников", "Гаврилов", "Ефимов", "Голубев", "Воронцов", "Зуев",
        "Беляков", "Калинин", "Лазарев", "Кудряшов", "Маслов", "Носов",
        "Шилов", "Климов", "Абрамов", "Фомин", "Денисов", "Гордеев",
        "Самойлов", "Князев", "Громов", "Кириллов", "Дроздов", "Игнатов",
        "Савельев", "Логинов", "Сафонов", "Прохоров", "Наумов", "Ширяев",
        "Овчинников", "Тихонов", "Тимофеев", "Дмитриев", "Крылов", "Карпов",
        "Власов", "Мартынов", "Соболев", "Бирюков", "Субботин",
    };

    /// <summary>
    /// Builds a RU identity: Cyrillic full name with gender agreement
    /// (<c>Дмитрий Иванов</c> / <c>Анна Иванова</c>) paired with a
    /// diverse-format email that does NOT correlate with the name — just
    /// like a real person whose name is "Дмитрий" but email is
    /// "phantom42@gmail.com". Fresh per call.
    /// </summary>
    private static (string Name, string Email) GenerateRussianIdentity()
    {
        var male  = Random.Shared.Next(2) == 0;
        var first = Pick(male ? MaleFirstNamesRu : FemaleFirstNamesRu);
        var last  = Pick(LastNamesRu) + (male ? "" : "а");
        var name  = $"{first} {last}";
        return (name, GenerateDiverseEmail());
    }

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
