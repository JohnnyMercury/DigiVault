using System.Text.Json;
using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Infrastructure.Data;
using DigiVault.Web.Services.Fulfilment;
using DigiVault.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Services;

public class PurchaseResult
{
    public bool Success { get; set; }
    public string? OrderNumber { get; set; }
    public string? ProductKey { get; set; }
    public string? ErrorMessage { get; set; }
    public decimal? NewBalance { get; set; }

    /// <summary>
    /// For external-payment purchases: where to redirect the user so they can
    /// pay on the provider's hosted page (Enot, YooKassa, etc.).
    /// </summary>
    public string? RedirectUrl { get; set; }

    /// <summary>For external-payment purchases: our internal Order id.</summary>
    public int? OrderId { get; set; }
}

public interface IOrderService
{
    Task<OrderViewModel?> GetOrderAsync(string userId, int orderId);
    Task<OrderViewModel?> GetOrderByNumberAsync(string userId, string orderNumber);
    Task<OrderHistoryViewModel> GetOrderHistoryAsync(string userId, int page = 1, int pageSize = 10);
    Task<List<TransactionViewModel>> GetTransactionsAsync(string userId, int count = 20);
    Task<PurchaseResult> CreatePurchaseAsync(string userId, int gameProductId, int quantity, string? deliveryInfo);

    /// <summary>
    /// Creates an Order in <see cref="OrderStatus.Pending"/> and asks the
    /// payment provider for a hosted-checkout URL. The user is redirected
    /// there; on successful webhook the order moves to Processing →
    /// Completed via the fulfilment pipeline.
    /// <paramref name="enotService"/> - optional Enot service code to lock
    /// the checkout to a single payment system (e.g. <c>"card"</c>, <c>"sbp"</c>).
    /// <paramref name="providerName"/> - explicit PSP code from the step-2
    /// picker (<c>"enot"</c>, <c>"paymentlink"</c>). When set, overrides the
    /// "first match" lookup so users actually get the gateway they picked.
    /// </summary>
    Task<PurchaseResult> CreateExternalPurchaseAsync(string userId, int gameProductId, int quantity,
        string? deliveryInfo, string paymentMethod, string? enotService, string siteBaseUrl, string? clientIp,
        string? providerName = null);

    /// <summary>
    /// Special-case purchase for Steam Wallet top-up: the amount is supplied by
    /// the user's slider (not by a fixed catalogue product). We anchor the
    /// OrderItem to the hidden <c>steam-wallet</c> GameProduct so the existing
    /// fulfilment pipeline kicks in (CredentialGenerator routes by Slug to
    /// <c>ContactSupportCredential</c>), but UnitPrice / Order.TotalAmount come
    /// from <paramref name="customAmount"/>.
    /// <paramref name="steamLogin"/> is stored on Order.DeliveryInfo so the
    /// support operator sees which Steam account to credit.
    /// </summary>
    Task<PurchaseResult> CreateSteamWalletPurchaseAsync(string userId, decimal customAmount,
        string steamLogin, string paymentMethod, string? enotService, string siteBaseUrl, string? clientIp,
        string? providerName = null);
}

public class OrderService : IOrderService
{
    private readonly ApplicationDbContext _context;
    private readonly IBalanceService _balanceService;
    private readonly IFulfilmentService _fulfilment;
    private readonly DigiVault.Core.Interfaces.IPaymentProviderFactory _providerFactory;
    private readonly ILogger<OrderService> _logger;

    public OrderService(ApplicationDbContext context, IBalanceService balanceService,
        IFulfilmentService fulfilment, DigiVault.Core.Interfaces.IPaymentProviderFactory providerFactory,
        ILogger<OrderService> logger)
    {
        _context = context;
        _balanceService = balanceService;
        _fulfilment = fulfilment;
        _providerFactory = providerFactory;
        _logger = logger;
    }

    public async Task<PurchaseResult> CreatePurchaseAsync(string userId, int gameProductId, int quantity, string? deliveryInfo)
    {
        try
        {
            // 1. Получить продукт
            var gameProduct = await _context.GameProducts
                .FirstOrDefaultAsync(gp => gp.Id == gameProductId && gp.IsActive);

            if (gameProduct == null)
                return new PurchaseResult { Success = false, ErrorMessage = "Товар не найден или недоступен" };

            if (gameProduct.StockQuantity < quantity)
                return new PurchaseResult { Success = false, ErrorMessage = "Товар временно недоступен (нет в наличии)" };

            var totalPrice = gameProduct.Price * quantity;

            // 2. Проверить баланс
            var balance = await _balanceService.GetBalanceAsync(userId);
            if (balance < totalPrice)
                return new PurchaseResult { Success = false, ErrorMessage = $"Недостаточно средств. Необходимо: {totalPrice:N0} ₽, на балансе: {balance:N0} ₽" };

            // 3. Списать средства
            var deductResult = await _balanceService.DeductFundsAsync(userId, totalPrice, $"Покупка: {gameProduct.Name}");
            if (!deductResult)
                return new PurchaseResult { Success = false, ErrorMessage = "Ошибка при списании средств" };

            // 4. Создать заказ в состоянии Processing — оплата прошла, ждём доставку.
            //    Финальный статус Completed выставляет IFulfilmentService.
            var orderNumber = GenerateOrderNumber();
            var order = new Order
            {
                UserId = userId,
                OrderNumber = orderNumber,
                TotalAmount = totalPrice,
                Status = OrderStatus.Processing,
                CreatedAt = DateTime.UtcNow,
                DeliveryInfo = deliveryInfo
            };
            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // 5. Создать OrderItem (DeliveryStatus = Pending по умолчанию).
            var orderItem = new OrderItem
            {
                OrderId = order.Id,
                GameProductId = gameProductId,
                Quantity = quantity,
                UnitPrice = gameProduct.Price,
                TotalPrice = totalPrice
            };
            _context.OrderItems.Add(orderItem);

            // 6. Транзакция в кошельке.
            var transaction = new Transaction
            {
                UserId = userId,
                Amount = totalPrice,
                Type = TransactionType.Purchase,
                Description = $"Покупка: {gameProduct.Name}",
                OrderId = order.Id,
                CreatedAt = DateTime.UtcNow
            };
            _context.Transactions.Add(transaction);

            // 7. Уменьшить остаток.
            gameProduct.StockQuantity -= quantity;
            await _context.SaveChangesAsync();

            // 8. Прогнать фулфилмент сразу — генерация payload синхронна и быстрая.
            //    Если упадёт по какой-то причине — фоновый OrderFulfilmentBackgroundService
            //    добьёт заказ в течение 30 секунд.
            await _fulfilment.DeliverOrderAsync(order.Id);

            var newBalance = await _balanceService.GetBalanceAsync(userId);

            _logger.LogInformation("Purchase completed: Order {OrderNumber}, User {UserId}, Product {ProductName}, Amount {Amount}",
                orderNumber, userId, gameProduct.Name, totalPrice);

            return new PurchaseResult
            {
                Success = true,
                OrderNumber = orderNumber,
                NewBalance = newBalance
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Purchase failed for user {UserId}, product {GameProductId}", userId, gameProductId);
            return new PurchaseResult { Success = false, ErrorMessage = "Произошла ошибка при оформлении заказа" };
        }
    }

    public async Task<PurchaseResult> CreateExternalPurchaseAsync(string userId, int gameProductId,
        int quantity, string? deliveryInfo, string paymentMethod, string? enotService,
        string siteBaseUrl, string? clientIp, string? providerName = null)
    {
        try
        {
            // 1. Validate product + stock
            var gameProduct = await _context.GameProducts
                .FirstOrDefaultAsync(gp => gp.Id == gameProductId && gp.IsActive);
            if (gameProduct == null)
                return new PurchaseResult { Success = false, ErrorMessage = "Товар не найден или недоступен" };
            if (gameProduct.StockQuantity < quantity)
                return new PurchaseResult { Success = false, ErrorMessage = "Товар временно недоступен (нет в наличии)" };

            var totalPrice = gameProduct.Price * quantity;

            // 2. Resolve the provider that handles this method.
            //    If the step-2 picker returned an explicit provider name (enot,
            //    paymentlink, …) honour it. Otherwise fall back to the first
            //    enabled provider that supports this method.
            var method = MapPaymentMethod(paymentMethod);
            DigiVault.Core.Interfaces.IPaymentProvider? provider = null;
            if (!string.IsNullOrWhiteSpace(providerName))
            {
                provider = _providerFactory.GetProvider(providerName);
                if (provider != null && !provider.SupportedMethods.Contains(method))
                {
                    _logger.LogWarning("Provider '{P}' doesn't support method {M} - falling back",
                        providerName, method);
                    provider = null;
                }
            }
            provider ??= _providerFactory.GetProviderForMethod(method);
            _logger.LogInformation(
                "External purchase: requested paymentMethod='{Raw}' provider='{Req}' → mapped to {Method}, picked provider '{Provider}' (enabled={Enabled})",
                paymentMethod, providerName ?? "(any)", method, provider?.Name ?? "(none)", provider?.IsEnabled);
            if (provider == null || !provider.IsEnabled)
                return new PurchaseResult { Success = false, ErrorMessage = "Способ оплаты временно недоступен" };

            // 3. Create the Order in Pending — payment hasn't arrived yet.
            //    Stock is decremented up-front to avoid two simultaneous checkouts
            //    going through; if the user abandons the flow we let the order
            //    expire naturally (no auto-refund logic for now).
            var orderNumber = GenerateOrderNumber();
            var order = new Order
            {
                UserId = userId,
                OrderNumber = orderNumber,
                TotalAmount = totalPrice,
                Status = OrderStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                DeliveryInfo = deliveryInfo
            };
            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            var orderItem = new OrderItem
            {
                OrderId = order.Id,
                GameProductId = gameProductId,
                Quantity = quantity,
                UnitPrice = gameProduct.Price,
                TotalPrice = totalPrice
            };
            _context.OrderItems.Add(orderItem);
            gameProduct.StockQuantity -= quantity;
            await _context.SaveChangesAsync();

            // 4. Ask the provider to create a payment. We pass our future webhook,
            //    success and fail URLs — the user lands back here after Enot.
            var user = await _context.Users.FindAsync(userId);
            var siteBase = siteBaseUrl.TrimEnd('/');
            var metadata = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(enotService))
                metadata["enot_service"] = enotService.Trim();

            // Per top-level UI category (card / sbp / qr / p2p / crypto / wallet),
            // hand the gateway the EXACT list of services to expose. Without this
            // we'd fall back to PaymentMethod-based filtering — but our enum
            // doesn't have QR or P2P values, so they'd collapse into SBP/Card and
            // the user would see the wrong options on Enot's checkout page.
            var enotServices = MapPaymentMethodToEnotServices(paymentMethod);
            if (enotServices.Length > 0)
                metadata["enot_services"] = string.Join(",", enotServices);

            var paymentRequest = new DigiVault.Core.Models.Payment.PaymentRequest
            {
                UserId      = userId,
                Amount      = totalPrice,
                Currency    = "RUB",
                Method      = method,
                Email       = user?.Email,
                Phone       = user?.PhoneNumber,
                // Generic neutral description — full orderNumber and product
                // name kept off the PSP-side payload to deny antifraud
                // brand-clustering. Reconciliation happens via
                // merchantTransactionId.
                Description = $"Покупка №{order.Id}",
                OrderId     = order.Id,
                SuccessUrl  = $"{siteBase}/Account/PaymentSuccess?orderId={order.Id}",
                CancelUrl   = $"{siteBase}/Account/PaymentFail?orderId={order.Id}",
                WebhookUrl  = $"{siteBase}/api/webhooks/{provider.Name}",
                ClientIp    = clientIp,
                Metadata    = metadata.Count > 0 ? metadata : null,
            };

            var providerResult = await provider.CreatePaymentAsync(paymentRequest);
            if (!providerResult.Success || string.IsNullOrEmpty(providerResult.RedirectUrl))
            {
                // Roll back the Order — provider rejected us.
                _context.OrderItems.Remove(orderItem);
                _context.Orders.Remove(order);
                gameProduct.StockQuantity += quantity;
                await _context.SaveChangesAsync();
                return new PurchaseResult { Success = false,
                    ErrorMessage = providerResult.ErrorMessage ?? "Не удалось создать платёж" };
            }

            // 5. Persist the PaymentTransaction so the webhook can find it.
            //    transaction.TransactionId == provider's order_id (what we control).
            //    transaction.ProviderTransactionId == provider's invoice id (what they return).
            _context.PaymentTransactions.Add(new PaymentTransaction
            {
                TransactionId         = providerResult.TransactionId ?? Guid.NewGuid().ToString("N"),
                ProviderTransactionId = providerResult.ProviderTransactionId,
                ProviderName          = provider.Name,
                UserId                = userId,
                OrderId               = order.Id,
                Method                = method,
                Status                = DigiVault.Core.Enums.PaymentStatus.Pending,
                Amount                = totalPrice,
                Currency              = "RUB",
                Description           = paymentRequest.Description,
                ClientIp              = clientIp,
                ProviderData          = providerResult.ProviderData != null
                                            ? JsonSerializer.Serialize(providerResult.ProviderData)
                                            : null,
                CreatedAt             = DateTime.UtcNow,
                UpdatedAt             = DateTime.UtcNow,
            });
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "External purchase initiated: Order {OrderNumber}, Provider {Provider}, User {UserId}, Amount {Amount}",
                orderNumber, provider.Name, userId, totalPrice);

            return new PurchaseResult
            {
                Success     = true,
                OrderNumber = orderNumber,
                OrderId     = order.Id,
                RedirectUrl = providerResult.RedirectUrl,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "External purchase failed for user {UserId}, product {GameProductId}",
                userId, gameProductId);
            return new PurchaseResult { Success = false, ErrorMessage = "Произошла ошибка при оформлении заказа" };
        }
    }

    public async Task<PurchaseResult> CreateSteamWalletPurchaseAsync(string userId, decimal customAmount,
        string steamLogin, string paymentMethod, string? enotService, string siteBaseUrl, string? clientIp,
        string? providerName = null)
    {
        try
        {
            // Slider bounds + bonus are admin-editable in AppSettings (steam:*).
            // Read once and validate, fall back to original hard-coded defaults
            // if not yet seeded.
            decimal SteamSetting(string key, decimal fallback)
            {
                var v = _context.AppSettings.AsNoTracking().FirstOrDefault(s => s.Key == key)?.Value;
                if (string.IsNullOrEmpty(v)) return fallback;
                return decimal.TryParse(v, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
            }
            var minBound  = SteamSetting("steam:min_amount",   100m);
            var maxBound  = SteamSetting("steam:max_amount",   15000m);
            var bonusPct  = SteamSetting("steam:bonus_percent", 10m);
            var bonusMult = 1m + bonusPct / 100m;

            if (customAmount < minBound)
                return new PurchaseResult { Success = false, ErrorMessage = $"Минимальная сумма пополнения - {minBound:N0} ₽" };
            if (customAmount > maxBound)
                return new PurchaseResult { Success = false, ErrorMessage = $"Максимальная сумма пополнения - {maxBound:N0} ₽" };
            if (string.IsNullOrWhiteSpace(steamLogin))
                return new PurchaseResult { Success = false, ErrorMessage = "Укажите логин Steam-аккаунта" };

            var anchor = await _context.GameProducts
                .Include(p => p.GiftCard)
                .FirstOrDefaultAsync(p => p.GiftCard != null && p.GiftCard.Slug == "steam-wallet");
            if (anchor == null)
                return new PurchaseResult { Success = false, ErrorMessage = "Товар временно недоступен" };

            // Visual «bonus» — charge customAmount, operator credits
            // customAmount * bonusMult on Steam side.
            var bonusedDisplay = $"{Math.Round(customAmount * bonusMult, 0):N0} ₽ на Steam";

            var method = MapPaymentMethod(paymentMethod);
            DigiVault.Core.Interfaces.IPaymentProvider? provider = null;
            if (!string.IsNullOrWhiteSpace(providerName))
            {
                provider = _providerFactory.GetProvider(providerName);
                if (provider != null && !provider.SupportedMethods.Contains(method))
                    provider = null;
            }
            provider ??= _providerFactory.GetProviderForMethod(method);
            if (provider == null || !provider.IsEnabled)
                return new PurchaseResult { Success = false, ErrorMessage = "Способ оплаты временно недоступен" };

            var orderNumber = GenerateOrderNumber();
            var deliveryInfo = $"Steam: {steamLogin.Trim()}; Зачислить: {bonusedDisplay}";

            var order = new Order
            {
                UserId       = userId,
                OrderNumber  = orderNumber,
                TotalAmount  = customAmount,
                Status       = OrderStatus.Pending,
                CreatedAt    = DateTime.UtcNow,
                DeliveryInfo = deliveryInfo
            };
            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            var orderItem = new OrderItem
            {
                OrderId       = order.Id,
                GameProductId = anchor.Id,
                Quantity      = 1,
                UnitPrice     = customAmount,
                TotalPrice    = customAmount,
            };
            _context.OrderItems.Add(orderItem);
            await _context.SaveChangesAsync();

            var user = await _context.Users.FindAsync(userId);
            var siteBase = siteBaseUrl.TrimEnd('/');
            var metadata = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(enotService))
                metadata["enot_service"] = enotService.Trim();
            var enotServices = MapPaymentMethodToEnotServices(paymentMethod);
            if (enotServices.Length > 0)
                metadata["enot_services"] = string.Join(",", enotServices);

            var paymentRequest = new DigiVault.Core.Models.Payment.PaymentRequest
            {
                UserId      = userId,
                Amount      = customAmount,
                Currency    = "RUB",
                Method      = method,
                Email       = user?.Email,
                // Generic neutral description (see note in CreateExternal-
                // PaymentOrderAsync above).
                Description = $"Пополнение №{order.Id}",
                OrderId     = order.Id,
                SuccessUrl  = $"{siteBase}/Account/PaymentSuccess?orderId={order.Id}",
                CancelUrl   = $"{siteBase}/Account/PaymentFail?orderId={order.Id}",
                WebhookUrl  = $"{siteBase}/api/webhooks/{provider.Name}",
                ClientIp    = clientIp,
                Metadata    = metadata.Count > 0 ? metadata : null,
            };

            var providerResult = await provider.CreatePaymentAsync(paymentRequest);
            if (!providerResult.Success || string.IsNullOrEmpty(providerResult.RedirectUrl))
            {
                _context.OrderItems.Remove(orderItem);
                _context.Orders.Remove(order);
                await _context.SaveChangesAsync();
                return new PurchaseResult { Success = false,
                    ErrorMessage = providerResult.ErrorMessage ?? "Не удалось создать платёж" };
            }

            _context.PaymentTransactions.Add(new PaymentTransaction
            {
                TransactionId         = providerResult.TransactionId ?? Guid.NewGuid().ToString("N"),
                ProviderTransactionId = providerResult.ProviderTransactionId,
                ProviderName          = provider.Name,
                UserId                = userId,
                OrderId               = order.Id,
                Method                = method,
                Status                = DigiVault.Core.Enums.PaymentStatus.Pending,
                Amount                = customAmount,
                Currency              = "RUB",
                Description           = paymentRequest.Description,
                ClientIp              = clientIp,
                ProviderData          = providerResult.ProviderData != null
                                            ? JsonSerializer.Serialize(providerResult.ProviderData)
                                            : null,
                CreatedAt             = DateTime.UtcNow,
                UpdatedAt             = DateTime.UtcNow,
            });
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Steam Wallet purchase initiated: Order {OrderNumber}, User {UserId}, Amount {Amount} → {Bonused}",
                orderNumber, userId, customAmount, bonusedDisplay);

            return new PurchaseResult
            {
                Success     = true,
                OrderNumber = orderNumber,
                OrderId     = order.Id,
                RedirectUrl = providerResult.RedirectUrl,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Steam Wallet purchase failed for user {UserId}, amount {Amount}", userId, customAmount);
            return new PurchaseResult { Success = false, ErrorMessage = "Произошла ошибка при оформлении заказа" };
        }
    }

    // Single source of truth: DigiVault.Web.Services.Payment.PaymentMethodCatalog.
    // Add / remove / rename methods there — mapping here updates automatically.
    private static DigiVault.Core.Enums.PaymentMethod MapPaymentMethod(string raw)
        => DigiVault.Web.Services.Payment.PaymentMethodCatalog.ToEnum(raw);

    /// <summary>
    /// Top-level UI category → list of Enot service codes actually enabled in
    /// our merchant cabinet (verified against
    /// <c>GET https://api.enot.io/shops/{shopId}/payment-tariffs</c>).
    /// Only services with <c>status=enabled</c> here are exposed to users.
    /// Re-check when tariffs change in the Enot cabinet.
    /// </summary>
    private static string[] MapPaymentMethodToEnotServices(string raw) => raw?.ToLowerInvariant() switch
    {
        "card" => new[] { "card" },                         // Visa / MC / МИР — единый карточный тариф Enot
        "sbp"  => new[] { "sbp" },                          // СБП — Enot рендерит в т.ч. как QR-код в hosted-checkout
        _      => Array.Empty<string>(),
    };

    public static string GenerateOrderNumber()
    {
        return $"DV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}";
    }

    public async Task<OrderViewModel?> GetOrderAsync(string userId, int orderId)
    {
        var order = await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.GameProduct)
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.ProductKeys)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId);

        return order != null ? MapToViewModel(order) : null;
    }

    public async Task<OrderViewModel?> GetOrderByNumberAsync(string userId, string orderNumber)
    {
        var order = await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.GameProduct)
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.ProductKeys)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber && o.UserId == userId);

        return order != null ? MapToViewModel(order) : null;
    }

    public async Task<OrderHistoryViewModel> GetOrderHistoryAsync(string userId, int page = 1, int pageSize = 10)
    {
        var query = _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.GameProduct)
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt);

        var totalOrders = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalOrders / (double)pageSize);

        var orders = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new OrderHistoryViewModel
        {
            Orders = orders.Select(MapToViewModel).ToList(),
            CurrentPage = page,
            TotalPages = totalPages
        };
    }

    public async Task<List<TransactionViewModel>> GetTransactionsAsync(string userId, int count = 20)
    {
        var transactions = await _context.Transactions
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(count)
            .ToListAsync();

        return transactions.Select(t => new TransactionViewModel
        {
            Id = t.Id,
            Amount = t.Amount,
            Type = t.Type,
            Description = t.Description,
            CreatedAt = t.CreatedAt
        }).ToList();
    }

    private static OrderViewModel MapToViewModel(Order order)
    {
        return new OrderViewModel
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            TotalAmount = order.TotalAmount,
            Status = order.Status,
            CreatedAt = order.CreatedAt,
            CompletedAt = order.CompletedAt,
            Items = order.OrderItems.Select(oi => new OrderItemViewModel
            {
                ProductId = oi.GameProductId,
                ProductName = oi.GameProduct?.Name ?? "Unknown",
                ImageUrl = oi.GameProduct?.ImageUrl,
                Quantity = oi.Quantity,
                UnitPrice = oi.UnitPrice,
                TotalPrice = oi.TotalPrice,
                DeliveryStatus = oi.DeliveryStatus,
                Delivery = DeliveryPayload.Deserialize(oi.DeliveryPayloadJson),
                ProductKeys = oi.ProductKeys.Select(k => k.KeyValue).ToList()
            }).ToList()
        };
    }
}
