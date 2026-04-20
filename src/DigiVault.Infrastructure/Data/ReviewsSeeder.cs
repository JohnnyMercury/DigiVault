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

    private static readonly (string Title, string Text)[] TelegramStarsPositive =
    {
        ("Быстро зачислили Stars", "Покупал 500 звёзд, всё пришло на аккаунт минут за 5. Цена заметно ниже чем в самом телеге. Буду брать ещё."),
        ("Всё работает", "Впервые покупал звёзды тут. Ввёл юзернейм, оплатил, через пару минут всё на месте. Удобно."),
        ("Норм сервис", "Брал 1000 звёзд. Зачислились сразу, никаких проблем. Рекомендую."),
        ("Выгоднее чем в Telegram", "Пересчитал — через сайт выходит процентов на 25 дешевле чем покупать напрямую. Постоянно тут беру."),
        ("Всё четко", "Оплата прошла без проблем, звёзды прилетели почти мгновенно. Саппорт на связи был, хоть и не понадобился."),
        ("Анонимно и быстро", "Плюс что можно анонимно купить, никто не видит кто отправил. Отлично для подарков."),
        ("Покупал 2500 звёзд", "Большой пакет со скидкой. Пришло без заминок. Подарил другу на ДР, он был рад."),
        ("Быстрее чем ожидал", "Думал будет задержка минут 10-15, а пришло за 2 минуты буквально. Круто."),
        ("Всё ок", "Купил 100 звёзд, пришли сразу. Всё работает, претензий нет."),
        ("Постоянный покупатель", "Беру звёзды уже третий раз, ни разу не было косяков. Цены адекватные."),
        ("Рекомендую", "Быстро, недорого, работает. Что ещё нужно? Спасибо."),
        ("Спасибо за сервис", "Помогли разобраться когда ввёл неверный юзернейм. Саппорт быстро ответил, отзывчивые ребята."),
        ("Отличная цена", "Сравнивал с другими сайтами — тут выходит выгоднее всего. Брать тут безопасно."),
        ("Всё пришло", "250 звёзд, зачислили за пару минут. Никакого обмана."),
        ("Быстрая оплата", "Оплатил через СБП, мгновенно. Звёзды на аккаунте. Буду возвращаться."),
    };

    private static readonly (string Title, string Text)[] TelegramPremiumPositive =
    {
        ("Premium активировался сразу", "Оплатил 3 месяца Telegram Premium. Пришло уведомление через 30 секунд. Всё работает."),
        ("Годовая подписка", "Брал на 12 месяцев, цена супер. Активация заняла меньше минуты."),
        ("Быстро и надёжно", "Уже второй раз продлеваю тут. Удобнее чем через App Store, и дешевле."),
        ("Премиум работает", "Все плюшки подтянулись — быстрая загрузка, больше папок, транскрибация голосовых. Доволен."),
        ("Супер цена", "За год вышло дешевле чем за пол-года через айтюнс. Прям выручили."),
        ("Всё ок", "Активировалось моментально. Использую уже месяц, проблем нет."),
        ("6 месяцев Premium", "Всё прошло гладко, подписка зачислилась, работает как надо."),
        ("Подарил маме", "Купил на её номер Premium, чтобы могла голосовые в текст переводить. Всё активировалось без её участия."),
        ("Удобно", "Ввёл ник, оплатил, через минуту в Telegram увидел значок Premium. Как и обещали."),
        ("Рекомендую", "Давно искал где подешевле купить. Нашёл тут, буду продлевать только здесь."),
        ("Быстро разобрались", "Случайно не тот юзернейм ввёл, написал в поддержку — подправили за 10 минут. Спасибо."),
        ("Работает стабильно", "Premium уже 2 месяца, ни одной проблемы, лимиты никуда не падают."),
    };

    private static readonly (string Title, string Text)[] FortnitePositive =
    {
        ("V-Bucks пришли быстро", "Купил 1000 V-Bucks, зачислили на Epic за 5 минут. Сын сразу побежал скин покупать."),
        ("Отличный сервис", "2800 V-Bucks, всё ок. Единственное — пришлось чуть подождать, но в пределах обещанного."),
        ("Выгодно", "Сравнил с Epic Store — тут процентов на 15-20 дешевле. Буду брать регулярно."),
        ("Всё четко", "Оплата через баланс, мгновенное зачисление. Удобно."),
        ("Быстрая доставка", "5000 V-Bucks, пришли минут за 7. Скин успел взять пока был в магазине."),
        ("Постоянный клиент", "Беру V-Bucks для сына тут каждый сезон. Ни разу не подвели."),
        ("Нормальный магазин", "Цены ок, доставка быстрая. Всё как обещают."),
        ("Для ребёнка берём", "Дочке купили, она играет на Switch. Всё пришло нормально, использовала."),
        ("Спасибо", "Первый раз, немного переживал, но всё прошло гладко. Теперь буду только тут."),
        ("Быстро", "Реально быстро, минут 3-4 и всё на аккаунте. Пользуюсь третий раз."),
    };

    private static readonly (string Title, string Text)[] RobloxPositive =
    {
        ("Robux за минуту", "Купил 1000 робуксов, пришли почти сразу. Ребёнок счастлив."),
        ("Всё ок", "Оплатил, ввёл ник роблокса, через 2 минуты Robux уже на аккаунте."),
        ("Выгодно", "В самом Roblox покупать дороже получается. Тут экономия процентов 20."),
        ("Быстро и просто", "Интерфейс понятный, всё по шагам. Сын сам разобрался."),
        ("Постоянно беру", "Третий раз пополняю Robux тут. Всё всегда нормально."),
        ("Для сына", "Быстрая доставка, проблем не было. Ребёнок рад."),
        ("Отлично", "400 робуксов, пришли за минуту после оплаты."),
        ("Рекомендую", "Лучше чем переплачивать в самом Roblox. Буду возвращаться."),
    };

    private static readonly (string Title, string Text)[] PubgPositive =
    {
        ("UC пришли быстро", "1500 UC зачислились почти моментально. Успел взять пропуск."),
        ("Всё ок", "Купил UC, пришло нормально. Цена адекватная."),
        ("Быстро", "Ввёл PlayerID, оплатил, через пару минут всё на аккаунте."),
        ("Выгодно", "В самой игре дороже. Тут экономия заметная."),
        ("Постоянный покупатель", "Беру тут UC каждый сезон Royal Pass. Ни разу не было косяков."),
        ("Нормально", "Всё пришло, без задержек. Спасибо."),
        ("Быстрая доставка", "660 UC, зачислили в течение минуты."),
    };

    private static readonly (string Title, string Text)[] GenshinPositive =
    {
        ("Genesis Crystals пришли", "Купил кристаллы, зачислились сразу. Взял благословение лунной тени."),
        ("Отлично", "Оплатил через баланс, всё пришло на аккаунт mihoyo быстро."),
        ("Быстро", "Нужны были срочно на банер, пришли за 5 минут. Спасибо."),
        ("Норм", "3280 Genesis, цена ок, доставка быстрая."),
        ("Выгодно", "Дешевле чем через официальный магазин miHoYo. Регулярно беру."),
        ("Всё гладко", "Ни одной проблемы, всё прозрачно. Рекомендую."),
    };

    private static readonly (string Title, string Text)[] HonkaiPositive =
    {
        ("Oneiric Shards зачислились", "Купил осколки, пришли сразу. Успел на баннер."),
        ("Быстро", "Всё пришло за пару минут. Цена нормальная."),
        ("Рекомендую", "Давно ищу где удобно покупать HSR, тут нашёл. Буду возвращаться."),
        ("Всё ок", "Нормальный сервис, оплата прошла, осколки на месте."),
    };

    private static readonly (string Title, string Text)[] MobileLegendsPositive =
    {
        ("Алмазы пришли быстро", "Купил Diamonds, зачислились на аккаунт моментально."),
        ("Отлично", "Оплатил, ввёл ID, всё прилетело без проблем."),
        ("Для новой героини", "Нужны были алмазы на Натана, всё быстро. Спасибо."),
        ("Всё норм", "Брал 878 алмазов, цена адекватная, доставка быстрая."),
    };

    private static readonly (string Title, string Text)[] PsnPositive =
    {
        ("PSN работает", "Купил карту PSN на 50 USD USA, активировалась без проблем. Взял игру в распродаже."),
        ("Быстрая доставка", "Код пришёл на почту через 2 минуты после оплаты. Активировал, деньги на счету."),
        ("Всё ок", "Брал 20$ PSN, всё отлично. Удобный регион, много скидок на Турцию сейчас."),
        ("Выгоднее официального", "Через регион USA карта выходит намного дешевле чем покупать игру напрямую."),
        ("Рекомендую", "Не первый раз беру. Код рабочий, активация мгновенная."),
        ("Постоянно тут", "PS Plus 12 месяцев на Турцию — дешевле в разы. Спасибо."),
        ("PS Plus Deluxe", "Оформил годовой Deluxe, активировался без проблем. Играю уже месяц."),
        ("Для PS5", "Пополнил счёт на 100$, взял GTA V + Cyberpunk. Очень доволен."),
    };

    private static readonly (string Title, string Text)[] XboxPositive =
    {
        ("Game Pass активировался", "Брал подписку на 3 месяца, код рабочий. Играю уже неделю в Starfield."),
        ("Отличная цена", "Game Pass Ultimate на год вышло дешевле чем платить помесячно. Выгодно."),
        ("Всё работает", "Купил карту Xbox на 25$, пополнил счёт, всё норм."),
        ("Быстро", "Код пришёл в течение минуты после оплаты."),
        ("Рекомендую", "Беру Game Pass тут уже второй год. Никаких нареканий."),
        ("Всё ок", "Активировалось сразу, играю без проблем."),
    };

    private static readonly (string Title, string Text)[] NintendoPositive =
    {
        ("Nintendo eShop", "Купил карту на 50 USD, пополнил счёт eShop. Взял Zelda и Mario."),
        ("Быстро пришло", "Код на eShop отправили почти сразу. Активировал без проблем."),
        ("Всё ок", "Регион работает, игры покупаются. Рекомендую."),
        ("Для Switch", "Ребёнку купили пополнение на eShop, всё активировалось нормально."),
    };

    private static readonly (string Title, string Text)[] NetflixPositive =
    {
        ("Подписка работает", "Купил на месяц, активировалась без проблем. Смотрю уже вторую неделю."),
        ("Быстро активировали", "Получил логин-пароль через 5 минут после оплаты. Всё работает."),
        ("Всё гладко", "Premium подписка, 4K работает, 4 устройства — всё как положено."),
        ("Дешевле чем официально", "Экономия заметная. Качество стриминга такое же как при прямой подписке."),
        ("Рекомендую", "Уже 3 месяца пользуюсь, ни одной проблемы. Буду продлевать."),
    };

    private static readonly (string Title, string Text)[] SpotifyPositive =
    {
        ("Premium активировался", "Купил на 3 месяца, подписка активировалась сразу. Рекламы нет, качество звука отличное."),
        ("Всё ок", "Spotify Premium работает как надо. Оффлайн треки качаются без проблем."),
        ("Быстро", "Активация заняла буквально минуту. Слушаю в удовольствие."),
        ("Годовая подписка", "Брал на год, цена супер. Работает во всех регионах."),
    };

    private static readonly (string Title, string Text)[] ApplePositive =
    {
        ("iTunes карта", "Купил карту на 25$, пополнил аккаунт Apple. Купил приложение, всё ок."),
        ("Быстрая активация", "Код пришёл сразу, активировал без проблем на регионе USA."),
        ("Отлично", "Беру постоянно для пополнения Apple ID, дешевле чем напрямую в России."),
        ("App Store США", "Подписки стали доступны, оплачиваю всё через баланс. Спасибо."),
    };

    private static readonly (string Title, string Text)[] YoutubePositive =
    {
        ("YouTube Premium", "Подписка на 3 месяца, активировалась сразу. Рекламы нет, фоновое воспроизведение работает."),
        ("Всё ок", "Брал на полгода, вышло очень выгодно. Работает стабильно."),
        ("Рекомендую", "Дешевле чем через Google Play. Активация за пару минут."),
    };

    private static readonly (string Title, string Text)[] NordVpnPositive =
    {
        ("NordVPN на год", "Купил годовую подписку, ключ пришёл сразу. Подключение стабильное, скорость отличная."),
        ("Всё работает", "Пользуюсь уже третий месяц. Канал с сайтом ни разу не падал."),
        ("Для работы", "Нужен был для удалёнки, купил на 2 года со скидкой. Скорости хватает, серверов много."),
        ("Быстрая активация", "Ключ в течение 5 минут на почте, активировал аккаунт без проблем."),
        ("Рекомендую", "Брал тут уже второй раз подписку NordVPN, ни разу не подводили."),
        ("Двухлетняя подписка", "Экономия колоссальная. Ни одной проблемы за всё время."),
        ("Работает в России", "Все соцсети и Discord открываются стабильно. Доволен."),
    };

    private static readonly (string Title, string Text)[] ExpressVpnPositive =
    {
        ("ExpressVPN быстро", "Ключ пришёл за 10 минут, активировал лицензию. Подключение мгновенное."),
        ("Отлично", "Использую для стриминга Netflix USA. Скорость держит 4K без заиканий."),
        ("В отпуске выручил", "Купил перед отпуском в Таиланд, все сайты работают как дома."),
        ("Рекомендую", "Из всех VPN что пробовал — самый стабильный. Буду продлевать."),
        ("Годовая подписка", "Выгоднее чем через сам Express. Поддержка отвечала быстро когда спрашивал."),
    };

    private static readonly (string Title, string Text)[] SurfsharkPositive =
    {
        ("Surfshark на 2 года", "Купил на 2 года со скидкой, активировалось без проблем. Безлимит устройств это огонь."),
        ("Всё ок", "Подключение стабильное, серверов много, в России работает."),
        ("Для семьи", "Поставил всем дома — жене, детям. Неограниченные устройства и это круто."),
        ("Отлично", "Скорость достойная, плюс можно CleanWeb подключить — реклама реально уходит."),
        ("Рекомендую", "Лучшее соотношение цена/качество из всех VPN. Беру только тут."),
    };

    // Mild 4★ reviews (minor nitpicks)
    private static readonly (string Title, string Text)[] FourStarTexts =
    {
        ("Хорошо, но можно быстрее", "Всё пришло, работает. Но пришлось ждать минут 15, ожидал быстрее. В целом норм."),
        ("Нормально", "Сервис рабочий, но интерфейс можно улучшить — не сразу нашёл куда жать."),
        ("Всё ок, но саппорт медленный", "Товар пришёл нормально, но когда был вопрос, ответа ждал часа полтора."),
        ("Работает", "Купил, получил, активировал. Только название на чек не выводится, было бы удобно."),
        ("Норм", "В целом всё ок, только мобильная версия сайта иногда тормозит."),
        ("Хороший сервис", "Всё работает, цены адекватные. Минус что нет оплаты криптой."),
        ("Пришло с задержкой", "Обещали моментальную доставку, пришло минут через 20. Не критично."),
        ("Средненько", "Всё работает, но ничего выдающегося. Сравнимо с другими подобными сайтами."),
    };

    // 3★ — something went notably wrong but resolved
    private static readonly (string Title, string Text)[] ThreeStarTexts =
    {
        ("Пришло с задержкой", "Ждал минут 40, уже хотел в поддержку писать. В итоге всё пришло, но осадочек остался."),
        ("Саппорт", "Был вопрос по оплате, ответили только через несколько часов. Хотелось бы быстрее."),
        ("Так себе", "Пришло, работает, но был день когда сайт вообще не открывался. Неудобно."),
        ("Неплохо", "Товар нормальный, но процесс оплаты затянулся — отклонялось несколько раз."),
    };

    // 2★ / 1★ — real problems
    private static readonly (string Title, string Text)[] LowRatingTexts =
    {
        ("Долго ждал", "Пришлось ждать больше часа, хотя обещали моментально. В итоге саппорт решил вопрос, но нервы потрепали."),
        ("Проблема с оплатой", "Деньги списались, а товар не пришёл. Пришлось долго переписываться с поддержкой пока разобрались."),
        ("Не очень", "Функционал работает, но мне не понравился процесс. Ожидал большего."),
    };

    // Admin replies for low-rated reviews
    private static readonly string[] AdminRepliesLow =
    {
        "Здравствуйте! Приносим извинения за задержку. Передали ваш отзыв в службу поддержки, обязательно разберёмся с проблемой и улучшим процесс.",
        "Спасибо за обратную связь! Мы постоянно работаем над скоростью работы поддержки. Если ещё возникнут вопросы — пишите, ответим в приоритете.",
        "Здравствуйте! Жаль что у вас возникли сложности. Мы стремимся к моментальной доставке и работаем над тем чтобы такие ситуации не повторялись.",
        "Благодарим за отзыв! Разобрались с причиной задержки с провайдером. В качестве извинения начислили бонус на баланс — пользуйтесь.",
    };

    // Admin replies for positive reviews
    private static readonly string[] AdminRepliesHigh =
    {
        "Спасибо за отзыв! Рады что всё прошло гладко. Ждём вас снова!",
        "Благодарим за доверие! Стараемся держать планку скорости и качества. Приходите ещё.",
        "Спасибо! Нам очень приятно, что сервис оказался удобным. Всегда на связи если что-то понадобится.",
        "Рады что вам понравилось! Продолжаем работать над тем чтобы покупки были максимально простыми.",
        "Спасибо за тёплые слова! Ваш отзыв — лучшая мотивация для команды.",
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

    public static async Task SeedAsync(ApplicationDbContext context)
    {
        if (await context.ProductReviews.AnyAsync()) return;

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
        var tgStars = giftCards.FirstOrDefault(g => g.Slug == "telegram-stars");
        if (tgStars != null) AddReviews(50, TelegramStarsPositive, giftCardId: tgStars.Id);

        var tgPremium = giftCards.FirstOrDefault(g => g.Slug == "telegram-premium");
        if (tgPremium != null) AddReviews(25, TelegramPremiumPositive, giftCardId: tgPremium.Id);

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
    }
}
