using RockyTelecom.Domain.Enums;

namespace RockyTelecom.Application.Common.Abstractions;

/// <summary>
/// Payment gateway abstraction (D-06). Phase 4 ships a sandbox mock; real SSLCommerz / bKash /
/// Nagad implementations swap in behind this interface with configuration only.
/// Success is only ever granted from a verified IPN, never a browser redirect (FR-10).
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Gateway key used in the route, e.g. <c>sslcommerz</c>.</summary>
    string Key { get; }

    PaymentMethod Method { get; }

    /// <summary>Starts a payment and returns the hosted-page redirect the browser should follow.</summary>
    Task<PaymentInitResult> InitAsync(PaymentInitRequest request, CancellationToken cancellationToken = default);

    /// <summary>Verifies the signature of an IPN/callback payload.</summary>
    bool VerifySignature(IReadOnlyDictionary<string, string> payload);

    /// <summary>Normalises a gateway callback into a validated outcome (idempotent handler contract).</summary>
    Task<PaymentIpnResult> HandleIpnAsync(IReadOnlyDictionary<string, string> payload, CancellationToken cancellationToken = default);
}

public sealed record PaymentInitRequest(
    string OrderNo,
    decimal Amount,
    string Currency,
    string CustomerName,
    string CustomerPhone,
    string? CustomerEmail,
    string SuccessUrl,
    string FailUrl,
    string CancelUrl,
    string IpnUrl);

public sealed record PaymentInitResult(string GatewayPageUrl, string GatewayTransactionRef);

/// <summary>Resolves the configured gateway implementation for a route key (e.g. <c>bkash</c>).</summary>
public interface IPaymentGatewayResolver
{
    IPaymentGateway? Resolve(string key);
    IReadOnlyCollection<string> Keys { get; }
}

/// <summary>
/// Server-owned redirect/IPN URLs handed to a gateway. Never taken from the request, so a crafted
/// success URL cannot influence where the gateway sends the customer (or its IPN).
/// </summary>
public interface IPaymentUrlBuilder
{
    PaymentCallbackUrls Build(string gatewayKey);
}

public sealed record PaymentCallbackUrls(
    string SuccessUrl,
    string FailUrl,
    string CancelUrl,
    string IpnUrl);

public sealed record PaymentIpnResult(
    bool IsValid,
    string OrderNo,
    string? TrxId,
    decimal Amount,
    PaymentStatus Status,
    string RawPayload);

/// <summary>
/// Courier abstraction (D-06). v1 ships a mock that returns fake consignments; a real
/// Steadfast / Pathao / RedX client swaps in without touching checkout. Delivery pricing itself
/// comes from the flat <c>DeliveryZone</c> rows, not from the courier.
/// </summary>
public interface ICourierService
{
    /// <summary>Provider key used in configuration, e.g. <c>mock</c>.</summary>
    string Provider { get; }

    Task<CourierBooking> BookAsync(CourierBookingRequest request, CancellationToken cancellationToken = default);
    Task<CourierTracking> TrackAsync(string consignmentId, CancellationToken cancellationToken = default);
}

public sealed record CourierBookingRequest(string OrderNo, string RecipientName, string RecipientPhone, string Address, decimal CodAmount);
public sealed record CourierBooking(string ConsignmentId, string TrackingCode, string Status);
public sealed record CourierTracking(string ConsignmentId, string Status, IReadOnlyList<string> Events);

/// <summary>SMS/OTP sender. v1 logs to file plus a mock provider.</summary>
public interface ISmsSender
{
    Task SendAsync(string phone, string message, CancellationToken cancellationToken = default);
}

/// <summary>Transactional email sender (SMTP pickup / Mailtrap in v1).</summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default);
}
