namespace DigiVault.Infrastructure.Data;

/// <summary>
/// Pool of internet-style usernames for demo purposes (live feed, fake reviews, etc.).
/// Mix of Russian diminutives, transliterations, and typical gaming handles — similar style
/// to what you'd see on plati.market / funpay / steam community.
/// Keep realistic: no caps-lock-shouting, no slurs, no edgy "xXx_killer_xXx" extremes.
/// </summary>
public static class DemoUsernames
{
    public static readonly string[] All = new[]
    {
        // Russian diminutives / bytovye
        "Димон", "Санёк", "Лёха", "Ксюха", "Натаха", "Кирюха", "Вован", "Славик", "Денчик",
        "Максик", "Андрюха", "Толян", "Серёга", "Витёк", "Гена", "Рома", "Юля", "Катюха",
        "Настёна", "Артёмка", "Пашок", "Стасян", "Ромчик", "Лёня", "Лиза", "Мишаня",

        // Transliterated first name + number / suffix
        "Dmitriy228", "Andrey_92", "Max_Pro", "Artem_77", "Sergey88", "Vladimir97", "Oleg_Pro",
        "Kirill_K", "Ivan_777", "Pavel_M", "Roman_99", "Anton_Kh", "Vadik_05", "Alex_Msk",
        "Danila_07", "Igor_13", "Nikita2003", "Stas_M", "Yura_98", "Timur_01",

        // lowercase internet nicks
        "sashok", "denchik", "artemka", "den4ik", "maxik_ru", "andrey.k", "lena_spb",
        "olga_msk", "tema_ekb", "dimon228", "kostyan_77", "pasha2000", "vova_96",
        "alex.gm", "sanya_pro", "kirya_05", "tolik_rnd",

        // Gaming handles
        "RedDragon", "NightFox", "FrostBite", "ShadowWolf", "Viper", "PixelHunter", "NeonRider",
        "IronFist", "DarkPhoenix", "SilentStorm", "CyberFox", "MadMax", "ProGamer_RU",
        "ToxicLord", "GhostRider", "BlueFire", "LoneWolf77", "SniperKing", "PhantomZ",
        "KillerMax", "VoidWalker", "RustyBlade", "AceSpade", "Grim228",

        // Mixed Russian+Latin / cyrillic nicks
        "Димка_К", "Артёмыч", "Лёха77", "Серый_М", "Кирюха_RU", "Настяш",

        // Simple first-name style
        "Артём", "Даня", "Никита", "Егор", "Глеб", "Марк", "Тимур", "Валя", "Полина", "Света",
        "Илья", "Юра", "Валера", "Стас",
    };

    /// <summary>
    /// Returns a merged pool: real DB users (first) + demo names.
    /// Use this when you have few real users and want the live-feed to feel populated.
    /// </summary>
    public static IEnumerable<string> Merge(IEnumerable<string>? realUsers)
    {
        var list = new List<string>();
        if (realUsers != null) list.AddRange(realUsers);
        list.AddRange(All);
        return list.Distinct();
    }
}
