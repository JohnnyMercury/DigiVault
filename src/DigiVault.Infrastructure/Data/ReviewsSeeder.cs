using DigiVault.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Infrastructure.Data;

/// <summary>
/// Seeds ~250 realistic-looking demo reviews across all products.
/// Uses names from <see cref="DemoUsernames"/> and category-specific text templates.
/// Rating distribution mimics what's typical on real e-commerce sites:
/// ~78% five-stars, 16% four, 3% three, 2% two, 1% one.
/// </summary>
public static class ReviewsSeeder
{
    // ------------------------------------------------------------
    // Review text templates per category. Title + body pairs.
    // Mix of short and detailed, all sound like real gamer/buyer speech.
    // ------------------------------------------------------------

    // NOTE: Telegram Stars was removed from the catalog. Templates are no longer
    // referenced by SeedAsync; left here in case the product is reactivated later.
    private static readonly (string Title, string Text)[] TelegramStarsPositive_Unused =
    {
        ("Быстро зачислили Stars", "Покупал 500 звёзд, все пришло на аккаунт минут за 5."),
    };

    private static readonly (string Title, string Text)[] TelegramPremiumPositive =
    {
        ("Premium активировался сразу", "Оплатил 3 месяца Telegram Premium. Пришло уведомление через 30 секунд. Все работает."),
        ("Годовая подписка", "Брал на 12 месяцев, цена супер. Активация заняла меньше минуты"),
        ("Быстро и надежно", "Уже второй раз продлеваю тут. Удобнее чем через App Store, и дешевле"),
        ("Премиум работает", "Все плюшки подтянулись, быстрая загрузка, больше папок, транскрибация голосовых. Доволен."),
        ("супер цена", "За год вышло дешевле чем за пол года через айтюнс. Прям выручили)"),
        ("Все ок", "Активировалось моментально. Использую уже месяц проблем нет"),
        ("6 месяцев Premium", "Все прошло гладко, подписка зачислилась, работает как надо."),
        ("Подарил маме", "Купил на ее номер Premium, чтобы могла голосовые в текст переводить. Все активировалось без ее участия."),
        ("Удобно", "Ввел ник, оплатил через минуту в Telegram увидел значок Premium. Как и обещали"),
        ("Рекомендую", "Давно искал где подешевле купить. Нашел тут, буду продлевать только здесь"),
        ("Быстро разобрались", "Случайно не тот юзернейм ввел, написал в поддержку подправили за 10 минут. Спасибо."),
        ("Работает стабильно", "Premium уже 2 месяца ни одной проблемы лимиты никуда не падают"),
        ("четко всё", "Взял на пол года для себя. активировалось сразу, доволен"),
    };

    private static readonly (string Title, string Text)[] FortnitePositive =
    {
        ("V-Bucks пришли быстро", "Купил 1000 V-Bucks, зачислили на Epic за 5 минут. Сын сразу побежал скин покупать)"),
        ("Отличный сервис", "2800 V-Bucks, все ок. Единственное пришлось чуть подождать, но в пределах обещанного."),
        ("Выгодно", "Сравнил с Epic Store, тут процентов на 15-20 дешевле. Буду брать регулярно"),
        ("Все четко", "Оплата через баланс, мгновенное зачисление. Удобно"),
        ("Быстрая доставка", "5000 V-Bucks, пришли минут за 7. Скин успел взять пока был в магазине."),
        ("Постоянный клиент", "Беру V-Bucks для сына тут каждый сезон. ни разу не подвели"),
        ("нормальный магазин", "Цены ок, доставка быстрая. Все как обещают"),
        ("Для ребенка берем", "Дочке купили она играет на Switch. Все пришло нормально, использовала."),
        ("Спасибо", "Первый раз немного переживал но все прошло гладко. Теперь буду только тут"),
        ("Быстро", "Реально быстро минут 3-4 и все на аккаунте. Пользуюсь третий раз"),
        ("топ", "взял 2800 vbucks, все пришло за 5 минут. цена приятная"),
    };

    private static readonly (string Title, string Text)[] RobloxPositive =
    {
        ("Robux за минуту", "Купил 1000 робуксов, пришли почти сразу. Ребенок счастлив"),
        ("Все ок", "Оплатил, ввел ник роблокса, через 2 минуты Robux уже на аккаунте."),
        ("Выгодно", "В самом Roblox покупать дороже получается. Тут экономия процентов 20"),
        ("Быстро и просто", "Интерфейс понятный, все по шагам. Сын сам разобрался)"),
        ("постоянно беру", "Третий раз пополняю Robux тут. все всегда нормально"),
        ("Для сына", "Быстрая доставка проблем не было. Ребенок рад"),
        ("Отлично", "400 робуксов пришли за минуту после оплаты."),
        ("Рекомендую", "Лучше чем переплачивать в самом Roblox. Буду возвращаться"),
        ("спс", "робуксы прилетели за пару минут, все норм"),
    };

    private static readonly (string Title, string Text)[] PubgPositive =
    {
        ("UC пришли быстро", "1500 UC зачислились почти моментально. Успел взять пропуск"),
        ("Все ок", "Купил UC, пришло нормально. Цена адекватная"),
        ("Быстро", "Ввел PlayerID оплатил, через пару минут все на аккаунте."),
        ("Выгодно", "В самой игре дороже. тут экономия заметная"),
        ("Постоянный покупатель", "Беру тут UC каждый сезон Royal Pass. Ни разу не было косяков"),
        ("Нормально", "Все пришло без задержек. Спасибо)"),
        ("Быстрая доставка", "660 UC, зачислили в течение минуты"),
    };

    private static readonly (string Title, string Text)[] GenshinPositive =
    {
        ("Genesis Crystals пришли", "Купил кристаллы, зачислились сразу. Взял благословение лунной тени"),
        ("Отлично", "Оплатил через баланс, все пришло на аккаунт mihoyo быстро"),
        ("Быстро", "Нужны были срочно на банер, пришли за 5 минут. Спасибо"),
        ("норм", "3280 Genesis, цена ок, доставка быстрая"),
        ("Выгодно", "Дешевле чем через официальный магазин miHoYo. Регулярно беру."),
        ("Все гладко", "Ни одной проблемы, все прозрачно. Рекомендую"),
    };

    private static readonly (string Title, string Text)[] HonkaiPositive =
    {
        ("Oneiric Shards зачислились", "Купил осколки пришли сразу. Успел на баннер"),
        ("Быстро", "Все пришло за пару минут. Цена нормальная"),
        ("Рекомендую", "Давно ищу где удобно покупать HSR, тут нашел. Буду возвращаться."),
        ("Все ок", "Нормальный сервис оплата прошла осколки на месте"),
    };

    private static readonly (string Title, string Text)[] MobileLegendsPositive =
    {
        ("Алмазы пришли быстро", "Купил Diamonds, зачислились на аккаунт моментально"),
        ("Отлично", "Оплатил, ввел ID, все прилетело без проблем."),
        ("Для новой героини", "Нужны были алмазы на Натана все быстро. Спасибо"),
        ("Все норм", "Брал 878 алмазов цена адекватная доставка быстрая"),
    };

    private static readonly (string Title, string Text)[] PsnPositive =
    {
        ("PSN работает", "Купил карту PSN на 50 USD USA, активировалась без проблем. Взял игру в распродаже."),
        ("Быстрая доставка", "Код пришел на почту через 2 минуты после оплаты. Активировал, деньги на счету"),
        ("Все ок", "Брал 20$ PSN все отлично. Удобный регион, много скидок на Турцию сейчас"),
        ("Выгоднее официального", "Через регион USA карта выходит намного дешевле чем покупать игру напрямую."),
        ("Рекомендую", "Не первый раз беру. Код рабочий активация мгновенная"),
        ("Постоянно тут", "PS Plus 12 месяцев на Турцию дешевле в разы. Спасибо)"),
        ("PS Plus Deluxe", "Оформил годовой Deluxe, активировался без проблем. Играю уже месяц."),
        ("Для PS5", "Пополнил счет на 100$, взял GTA V + Cyberpunk. Очень доволен"),
    };

    private static readonly (string Title, string Text)[] XboxPositive =
    {
        ("Game Pass активировался", "Брал подписку на 3 месяца, код рабочий. Играю уже неделю в Starfield"),
        ("Отличная цена", "Game Pass Ultimate на год вышло дешевле чем платить помесячно. Выгодно"),
        ("Все работает", "Купил карту Xbox на 25$, пополнил счет все норм"),
        ("Быстро", "Код пришел в течение минуты после оплаты."),
        ("Рекомендую", "Беру Game Pass тут уже второй год. Никаких нареканий"),
        ("Все ок", "Активировалось сразу играю без проблем"),
    };

    private static readonly (string Title, string Text)[] NintendoPositive =
    {
        ("Nintendo eShop", "Купил карту на 50 USD, пополнил счет eShop. Взял Zelda и Mario"),
        ("Быстро пришло", "Код на eShop отправили почти сразу. Активировал без проблем."),
        ("Все ок", "Регион работает, игры покупаются. Рекомендую"),
        ("Для Switch", "Ребенку купили пополнение на eShop, все активировалось нормально"),
    };

    private static readonly (string Title, string Text)[] NetflixPositive =
    {
        ("Подписка работает", "Купил на месяц, активировалась без проблем. Смотрю уже вторую неделю"),
        ("Быстро активировали", "Получил логин-пароль через 5 минут после оплаты. Все работает"),
        ("Все гладко", "Premium подписка, 4K работает, 4 устройства, все как положено"),
        ("Дешевле чем официально", "Экономия заметная. Качество стриминга такое же как при прямой подписке."),
        ("Рекомендую", "Уже 3 месяца пользуюсь ни одной проблемы. Буду продлевать"),
    };

    private static readonly (string Title, string Text)[] SpotifyPositive =
    {
        ("Premium активировался", "Купил на 3 месяца, подписка активировалась сразу. Рекламы нет, качество звука отличное"),
        ("Все ок", "Spotify Premium работает как надо. Оффлайн треки качаются без проблем"),
        ("быстро", "Активация заняла буквально минуту. Слушаю в удовольствие)"),
        ("Годовая подписка", "Брал на год, цена супер. Работает во всех регионах."),
    };

    private static readonly (string Title, string Text)[] ApplePositive =
    {
        ("iTunes карта", "Купил карту на 25$, пополнил аккаунт Apple. Купил приложение, все ок"),
        ("Быстрая активация", "Код пришел сразу активировал без проблем на регионе USA"),
        ("Отлично", "Беру постоянно для пополнения Apple ID, дешевле чем напрямую в России"),
        ("App Store США", "Подписки стали доступны, оплачиваю все через баланс. Спасибо"),
    };

    private static readonly (string Title, string Text)[] YoutubePositive =
    {
        ("YouTube Premium", "Подписка на 3 месяца, активировалась сразу. Рекламы нет, фоновое воспроизведение работает."),
        ("Все ок", "Брал на полгода, вышло очень выгодно. Работает стабильно"),
        ("Рекомендую", "Дешевле чем через Google Play. Активация за пару минут"),
    };

    private static readonly (string Title, string Text)[] NordVpnPositive =
    {
        ("NordVPN на год", "Купил годовую подписку, ключ пришел сразу. Подключение стабильное, скорость отличная"),
        ("Все работает", "Пользуюсь уже третий месяц. Канал с сайтом ни разу не падал"),
        ("Для работы", "Нужен был для удаленки купил на 2 года со скидкой. Скорости хватает, серверов много"),
        ("Быстрая активация", "Ключ в течение 5 минут на почте, активировал аккаунт без проблем"),
        ("Рекомендую", "Брал тут уже второй раз подписку NordVPN, ни разу не подводили."),
        ("Двухлетняя подписка", "Экономия колоссальная. Ни одной проблемы за все время"),
        ("Работает в России", "Все соцсети и Discord открываются стабильно. Доволен"),
    };

    private static readonly (string Title, string Text)[] ExpressVpnPositive =
    {
        ("ExpressVPN быстро", "Ключ пришел за 10 минут, активировал лицензию. Подключение мгновенное."),
        ("Отлично", "Использую для стриминга Netflix USA. Скорость держит 4K без заиканий"),
        ("В отпуске выручил", "Купил перед отпуском в Таиланд все сайты работают как дома."),
        ("Рекомендую", "Из всех VPN что пробовал самый стабильный. Буду продлевать"),
        ("Годовая подписка", "Выгоднее чем через сам Express. Поддержка отвечала быстро когда спрашивал"),
    };

    private static readonly (string Title, string Text)[] SurfsharkPositive =
    {
        ("Surfshark на 2 года", "Купил на 2 года со скидкой активировалось без проблем. Безлимит устройств это огонь)"),
        ("Все ок", "Подключение стабильное серверов много, в России работает"),
        ("Для семьи", "Поставил всем дома жене детям. Неограниченные устройства и это круто"),
        ("Отлично", "Скорость достойная, плюс можно CleanWeb подключить, реклама реально уходит."),
        ("Рекомендую", "Лучшее соотношение цена/качество из всех VPN. Беру только тут"),
    };

    // Mild 4★ reviews (minor nitpicks)
    private static readonly (string Title, string Text)[] FourStarTexts =
    {
        ("Хорошо, но можно быстрее", "Все пришло работает. Но пришлось ждать минут 15, ожидал быстрее. В целом норм"),
        ("Нормально", "Сервис рабочий, но интерфейс можно улучшить, не сразу нашел куда жать"),
        ("Все ок но саппорт медленный", "Товар пришел нормально, но когда был вопрос, ответа ждал часа полтора"),
        ("работает", "Купил, получил, активировал. Только название на чек не выводится, было бы удобно."),
        ("Норм", "В целом все ок, только мобильная версия сайта иногда тормозит"),
        ("Хороший сервис", "Все работает, цены адекватные. Минус что нет оплаты криптой"),
        ("Пришло с задержкой", "Обещали моментальную доставку, пришло минут через 20. Не критично."),
        ("Средненько", "Все работает но ничего выдающегося. Сравнимо с другими подобными сайтами"),
    };

    // 3★ — something went notably wrong but resolved
    private static readonly (string Title, string Text)[] ThreeStarTexts =
    {
        ("Пришло с задержкой", "Ждал минут 40, уже хотел в поддержку писать. В итоге все пришло, но осадочек остался."),
        ("Саппорт", "Был вопрос по оплате, ответили только через несколько часов. Хотелось бы быстрее"),
        ("Так себе", "Пришло, работает, но был день когда сайт вообще не открывался. Неудобно."),
        ("Неплохо", "Товар нормальный, но процесс оплаты затянулся, отклонялось несколько раз"),
    };

    // 2★ / 1★ — real problems
    private static readonly (string Title, string Text)[] LowRatingTexts =
    {
        ("Долго ждал", "Пришлось ждать больше часа хотя обещали моментально. В итоге саппорт решил вопрос, но нервы потрепали"),
        ("Проблема с оплатой", "Деньги списались, а товар не пришел. Пришлось долго переписываться с поддержкой пока разобрались"),
        ("Не очень", "Функционал работает, но мне не понравился процесс. Ожидал большего."),
    };

    // Admin replies for low-rated reviews
    private static readonly string[] AdminRepliesLow =
    {
        "Здравствуйте! Приносим извинения за задержку. Передали ваш отзыв в службу поддержки, обязательно разберёмся с проблемой и улучшим процесс.",
        "Спасибо за обратную связь! Мы постоянно работаем над скоростью работы поддержки. Если ещё возникнут вопросы, пишите, ответим в приоритете.",
        "Здравствуйте! Жаль что у вас возникли сложности. Мы стремимся к моментальной доставке и работаем над тем чтобы такие ситуации не повторялись.",
        "Благодарим за отзыв! Разобрались с причиной задержки с провайдером. В качестве извинения начислили бонус на баланс, пользуйтесь.",
    };

    // Admin replies for positive reviews
    private static readonly string[] AdminRepliesHigh =
    {
        "Спасибо за отзыв! Рады что всё прошло гладко. Ждём вас снова!",
        "Благодарим за доверие! Стараемся держать планку скорости и качества. Приходите ещё.",
        "Спасибо! Нам очень приятно, что сервис оказался удобным. Всегда на связи если что-то понадобится.",
        "Рады что вам понравилось! Продолжаем работать над тем чтобы покупки были максимально простыми.",
        "Спасибо за тёплые слова! Ваш отзыв, лучшая мотивация для команды.",
    };

    // Author roles — used sparingly (not everyone has one)
    private static readonly string[] AuthorRoles =
    {
        "Постоянный клиент", "Покупатель с 2024 г.", "Клиент с начала года",
        "Геймер", "Стример", "Игрок в Fortnite", "Игрок в Dota 2",
        "Активный пользователь", "Проверенный покупатель", "Постоянный покупатель",
        "Клиент с опытом", "Зарегистрирован в 2024",
    };

    // ------------------------------------------------------------

    // Bump this when review templates change — forces existing demo reviews to be
    // wiped and reseeded. Real user-authored reviews (UserId != null) are never touched.
    private const string SeedVersion = "3";
    private const string SeedVersionKey = "reviews:seed_version";

    public static async Task SeedAsync(ApplicationDbContext context)
    {
        var versionSetting = await context.AppSettings.FirstOrDefaultAsync(s => s.Key == SeedVersionKey);
        if (versionSetting?.Value == SeedVersion) return; // already seeded with current templates

        // Wipe only demo reviews (UserId == null). Keep real ones.
        var demoReviews = await context.ProductReviews.Where(r => r.UserId == null).ToListAsync();
        if (demoReviews.Any())
        {
            context.ProductReviews.RemoveRange(demoReviews);
            await context.SaveChangesAsync();
        }

        var games = await context.Games.AsNoTracking().ToListAsync();
        var giftCards = await context.GiftCards.AsNoTracking().ToListAsync();
        var vpns = await context.VpnProviders.AsNoTracking().ToListAsync();

        var rnd = new Random(42); // deterministic seed
        var names = DemoUsernames.All;
        var reviews = new List<ProductReview>();

        // Helper to generate one review with the given template pool
        void AddReviews(int count, (string Title, string Text)[] positiveTemplates,
            int? gameId = null, int? giftCardId = null, int? vpnId = null)
        {
            for (int i = 0; i < count; i++)
            {
                // Rating distribution: 5★=78%, 4★=16%, 3★=3%, 2★=2%, 1★=1%
                var roll = rnd.Next(100);
                int rating;
                (string Title, string Text) tpl;

                if (roll < 78) { rating = 5; tpl = positiveTemplates[rnd.Next(positiveTemplates.Length)]; }
                else if (roll < 94) { rating = 4; tpl = FourStarTexts[rnd.Next(FourStarTexts.Length)]; }
                else if (roll < 97) { rating = 3; tpl = ThreeStarTexts[rnd.Next(ThreeStarTexts.Length)]; }
                else if (roll < 99) { rating = 2; tpl = LowRatingTexts[rnd.Next(LowRatingTexts.Length)]; }
                else { rating = 1; tpl = LowRatingTexts[rnd.Next(LowRatingTexts.Length)]; }

                var daysAgo = rnd.Next(90);
                var createdAt = DateTime.UtcNow.AddDays(-daysAgo).AddHours(-rnd.Next(24)).AddMinutes(-rnd.Next(60));

                // Admin replies: ~60% on low ratings, ~20% on high
                string? reply = null;
                DateTime? replyAt = null;
                var replyRoll = rnd.Next(100);
                if (rating <= 3 && replyRoll < 60)
                {
                    reply = AdminRepliesLow[rnd.Next(AdminRepliesLow.Length)];
                    replyAt = createdAt.AddHours(rnd.Next(2, 30));
                }
                else if (rating >= 4 && replyRoll < 20)
                {
                    reply = AdminRepliesHigh[rnd.Next(AdminRepliesHigh.Length)];
                    replyAt = createdAt.AddHours(rnd.Next(1, 48));
                }

                // Role: ~25% of reviews have one
                string? role = rnd.Next(100) < 25 ? AuthorRoles[rnd.Next(AuthorRoles.Length)] : null;

                reviews.Add(new ProductReview
                {
                    AuthorName = names[rnd.Next(names.Length)],
                    AuthorRole = role,
                    GameId = gameId,
                    GiftCardId = giftCardId,
                    VpnProviderId = vpnId,
                    Rating = rating,
                    Title = tpl.Title,
                    Text = tpl.Text,
                    HelpfulCount = rnd.Next(8, 310),
                    IsVerifiedPurchase = true,
                    IsApproved = true,
                    AdminReply = reply,
                    AdminReplyAt = replyAt,
                    CreatedAt = createdAt,
                });
            }
        }

        // ==== Games ====
        var fortnite = games.FirstOrDefault(g => g.Slug == "fortnite");
        if (fortnite != null) AddReviews(25, FortnitePositive, gameId: fortnite.Id);

        var roblox = games.FirstOrDefault(g => g.Slug == "roblox");
        if (roblox != null) AddReviews(20, RobloxPositive, gameId: roblox.Id);

        var pubg = games.FirstOrDefault(g => g.Slug == "pubg");
        if (pubg != null) AddReviews(15, PubgPositive, gameId: pubg.Id);

        var genshin = games.FirstOrDefault(g => g.Slug == "genshin");
        if (genshin != null) AddReviews(15, GenshinPositive, gameId: genshin.Id);

        var honkai = games.FirstOrDefault(g => g.Slug == "honkai");
        if (honkai != null) AddReviews(10, HonkaiPositive, gameId: honkai.Id);

        var mlbb = games.FirstOrDefault(g => g.Slug == "mobilelegends");
        if (mlbb != null) AddReviews(10, MobileLegendsPositive, gameId: mlbb.Id);

        // ==== Gift Cards ====
        // Telegram Stars removed from catalog — no reviews seeded for it anymore.
        // Bumped Premium count since it absorbs former stars traffic.
        var tgPremium = giftCards.FirstOrDefault(g => g.Slug == "telegram-premium");
        if (tgPremium != null) AddReviews(45, TelegramPremiumPositive, giftCardId: tgPremium.Id);

        var psn = giftCards.FirstOrDefault(g => g.Slug == "psn");
        if (psn != null) AddReviews(20, PsnPositive, giftCardId: psn.Id);

        var xbox = giftCards.FirstOrDefault(g => g.Slug == "xbox");
        if (xbox != null) AddReviews(15, XboxPositive, giftCardId: xbox.Id);

        var nintendo = giftCards.FirstOrDefault(g => g.Slug == "nintendo");
        if (nintendo != null) AddReviews(10, NintendoPositive, giftCardId: nintendo.Id);

        var netflix = giftCards.FirstOrDefault(g => g.Slug == "stream-cards");
        if (netflix != null) AddReviews(12, NetflixPositive, giftCardId: netflix.Id);

        var spotify = giftCards.FirstOrDefault(g => g.Slug == "spotify");
        if (spotify != null) AddReviews(10, SpotifyPositive, giftCardId: spotify.Id);

        var apple = giftCards.FirstOrDefault(g => g.Slug == "apple");
        if (apple != null) AddReviews(8, ApplePositive, giftCardId: apple.Id);

        var youtube = giftCards.FirstOrDefault(g => g.Slug == "youtube");
        if (youtube != null) AddReviews(8, YoutubePositive, giftCardId: youtube.Id);

        // ==== VPN ====
        var nord = vpns.FirstOrDefault(v => v.Slug == "nordvpn");
        if (nord != null) AddReviews(15, NordVpnPositive, vpnId: nord.Id);

        var express = vpns.FirstOrDefault(v => v.Slug == "expressvpn");
        if (express != null) AddReviews(12, ExpressVpnPositive, vpnId: express.Id);

        var surf = vpns.FirstOrDefault(v => v.Slug == "surfshark");
        if (surf != null) AddReviews(13, SurfsharkPositive, vpnId: surf.Id);

        context.ProductReviews.AddRange(reviews);
        await context.SaveChangesAsync();

        // Record seed version so we don't re-run next time unless templates change.
        if (versionSetting != null)
        {
            versionSetting.Value = SeedVersion;
            versionSetting.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            context.AppSettings.Add(new AppSetting
            {
                Key = SeedVersionKey,
                Value = SeedVersion,
                Description = "Reviews seeder templates version — bump to force reseed of demo reviews",
                UpdatedAt = DateTime.UtcNow,
            });
        }
        await context.SaveChangesAsync();
    }
}
