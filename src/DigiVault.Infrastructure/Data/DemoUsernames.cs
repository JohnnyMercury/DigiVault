namespace DigiVault.Infrastructure.Data;

/// <summary>
/// Pool of realistic usernames for demo purposes (live feed, fake reviews, etc.).
/// Based on actual nicks scraped from otzovik.com / tbank.ru review pages
/// for plati.market and funpay — i.e. how real Russian users of similar digital
/// marketplaces actually name themselves.
///
/// Mix: (1) real scraped handles, (2) plain first names (like on tbank reviews),
/// (3) name + digit / underscore / city patterns. Deliberately avoids cringy gaming
/// handles ("ShadowWolf", "NightFox") and stereotypical Russian slang diminutives
/// ("Димон", "Санёк", "Лёха").
/// </summary>
public static class DemoUsernames
{
    public static readonly string[] All = new[]
    {
        // Real scraped handles from otzovik reviews of plati.market / similar (batch 1)
        "Ziper19", "Mir5000", "Temka727", "Maikl929", "PavelGP", "AndreyZag",
        "BanzaiCh", "Score4fan", "Saumarel", "Schnee", "Sprini", "bubyshka",
        "orb1tR", "grey2035", "den391", "Azri666", "Ryazal", "LDreg",
        "Dorobolo", "TowelInSpace", "RaceGame", "DragaZloi", "dzmitrgimra",
        "gamespaytv", "retro125", "redchristmas", "Anon1388", "Superbober",
        "sidnik56", "FreeDie", "Achikurekus",

        // Real scraped handles (batch 2 — more otzovik pages)
        "Моисей128", "Loderunner1", "Ymiira", "mugmellman", "Xsero", "ArtEM-41",
        "yanderded", "FrozenDozen", "zixxGtR", "Ruslan121837", "Tirez", "pearlmist",
        "dk5574", "sharashka", "denozord", "HedgehogTop555", "Julfy206", "Voyzi",
        "particuuular", "Kuska47", "qwizzary", "Starmaster55", "Elmin1105",
        "Forto4nik", "Denekmax", "foreveryoung2727", "WooTaaN", "Krabissimo",
        "mansrua", "Kikinobubes", "v1kont", "olololol222333", "Cnackls",
        "dimasla", "Iavan", "Repos",

        // Real scraped handles (batch 3)
        "Beltros", "Kolobunka", "Tor999", "mfthesun", "Liza-25", "Mazda11",
        "gStone", "Synthez", "DanZee", "Artanis", "Levin08", "anotonantonoff",
        "Raksion", "kaelthaaas", "Dmitriy-E", "BREADDADY", "Kai Krapivnik",
        "Codificer", "kirilloffpk", "DMP2109", "Roman-PP", "Kapustka Masha",
        "gantelin", "NK86", "santanaahh", "SharpenedEdge", "medvedev325",
        "Phil Phil", "AntonIBob", "robson112233", "KiberWor", "AlexImplex",
        "Aspirin3419",

        // Real scraped handles (batch 4 — plati.ru)
        "Ева Браун", "xxrayss", "tydyber", "ГородВгороде", "Человек48", "jaraxxus",
        "UderCasus", "Aciago", "bablik94", "Toni21", "Kotalbest", "Dengeostv",
        "IntoTheSun", "Whatever2004", "DomashnyaYa", "Eugenia321", "roman231219800",
        "Sqvisi", "Aleksandr566", "dooldool", "Apple8888", "Layfield", "Ellis15",
        "Morkos", "orusselo", "ArlekinDen", "Inquisitor40000", "vkokosike",
        "Connor1", "14cthutq", "Daine", "Rosss82", "fenjo27", "n1cht", "Btodiaga",

        // Plain first names — how real users name themselves on tbank reviews
        "Даниил", "Никита", "Артём", "Кирилл", "Сергей", "Иван", "Богдан",
        "Степан", "Юрий", "Виктория", "Макар", "Георгий", "Алексей", "Михаил",
        "Дмитрий", "Владимир", "Екатерина", "Мария", "Елена", "Анна", "Андрей",
        "Максим", "Александр", "Ольга", "Наталья", "Павел", "Роман", "Илья",
        "Евгений", "Тимур", "Данила", "Ибрагим",

        // Name + digit / underscore / city — typical internet marketplace pattern
        "max_94", "alex_msk", "lena_2005", "andrey.k", "dima_777", "sergey88",
        "olga_spb", "petr_p", "vadim_k", "kirill_m", "nastya.a", "yulia_r",
        "roman_2k", "artem.g", "nikita_93", "denis.k", "vasya_01", "tanya_msk",
        "mihail_77", "pavel_ekb", "katya_nn", "igor_2003", "anton_k", "slava_rnd",
        "timur_99", "stas_msk", "gleb_98", "tema_spb", "vlad_k", "oleg_07",

        // Simple lowercase handles (seen often on marketplaces)
        "tema", "denchik", "artemka", "maxik", "kotik", "murka", "sanek",
        "vovan", "tolik", "kostya.m", "zhenya", "rusik",

        // Plain transliterations (also common)
        "Dmitriy", "Andrey", "Sergey", "Aleksey", "Mikhail", "Natalia",
        "Ekaterina", "Vladimir", "Kirill", "Pavel",
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
