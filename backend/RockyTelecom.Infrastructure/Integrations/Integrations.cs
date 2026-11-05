using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Domain.Enums;
using RockyTelecom.Infrastructure.Options;

namespace RockyTelecom.Infrastructure.Integrations;

/// <summary>
/// Sandbox gateway (D-06). It never decides that money arrived: it only normalises a callback into
/// a <see cref="PaymentIpnResult"/>, and the order handler still verifies amount and idempotency
/// before marking anything paid (FR-10).
/// </summary>
internal class MockPaymentGateway : IPaymentGateway
{
    private readonly PaymentOptions _options;
    private readonly string _key;
    private readonly PaymentMethod _method;

    public MockPaymentGateway(IOptions<PaymentOptions> options, string key, PaymentMethod method)
    {
        _options = options.Value;
        _key = key;
        _method = method;
    }

    public string Key => _key;

    public PaymentMethod Method => _method;

    public Task<PaymentInitResult> InitAsync(PaymentInitRequest request, CancellationToken cancellationToken = default)
    {
        var reference = $"MOCK-{request.OrderNo}-{Guid.NewGuid():N}";
        var baseUrl = string.IsNullOrWhiteSpace(_options.SandboxBaseUrl)
            ? "https://sandbox.rockytelecom.local/pay"
            : _options.SandboxBaseUrl.TrimEnd('/');

        var query = string.Join('&', new[]
        {
            $"ref={Uri.EscapeDataString(reference)}",
            $"amount={request.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"order={Uri.EscapeDataString(request.OrderNo)}",
            $"success={Uri.EscapeDataString(request.SuccessUrl)}",
            $"fail={Uri.EscapeDataString(request.FailUrl)}",
            $"cancel={Uri.EscapeDataString(request.CancelUrl)}",
            $"ipn={Uri.EscapeDataString(request.IpnUrl)}"
        });

        return Task.FromResult(new PaymentInitResult($"{baseUrl}?{query}", reference));
    }

    public bool VerifySignature(IReadOnlyDictionary<string, string> payload)
    {
        // Fail closed. An unconfigured secret means the deployment is not ready to accept a
        // callback at all — accepting it would let anyone mark an order paid (FR-10, D-07).
        if (string.IsNullOrWhiteSpace(_options.IpnSecret))
        {
            return false;
        }

        if (!payload.TryGetValue("signature", out var signature) || string.IsNullOrEmpty(signature))
        {
            return false;
        }

        var expected = System.Text.Encoding.UTF8.GetBytes(_options.IpnSecret);
        var actual = System.Text.Encoding.UTF8.GetBytes(signature);

        return expected.Length == actual.Length
               && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public Task<PaymentIpnResult> HandleIpnAsync(
        IReadOnlyDictionary<string, string> payload, CancellationToken cancellationToken = default)
    {
        payload.TryGetValue("orderNo", out var orderNo);
        payload.TryGetValue("trxId", out var trxId);
        payload.TryGetValue("status", out var status);

        var amount = payload.TryGetValue("amount", out var rawAmount)
                     && decimal.TryParse(rawAmount, System.Globalization.NumberStyles.Number,
                         System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;

        var paymentStatus = status?.ToLowerInvariant() switch
        {
            "paid" or "success" or "valid" => PaymentStatus.Paid,
            "failed" => PaymentStatus.Failed,
            "cancelled" => PaymentStatus.Cancelled,
            "refunded" => PaymentStatus.Refunded,
            _ => PaymentStatus.Pending
        };

        var isValid = VerifySignature(payload)
                      && !string.IsNullOrWhiteSpace(orderNo)
                      && (!string.IsNullOrWhiteSpace(trxId) || paymentStatus != PaymentStatus.Paid);

        return Task.FromResult(new PaymentIpnResult(
            isValid,
            orderNo ?? string.Empty,
            trxId,
            amount,
            paymentStatus,
            System.Text.Json.JsonSerializer.Serialize(payload)));
    }
}

/// <summary>Sandbox SSLCommerz (cards/bank/MFS). Swaps to the real client by configuration only.</summary>
internal sealed class MockSslCommerzGateway : MockPaymentGateway
{
    public MockSslCommerzGateway(IOptions<PaymentOptions> options)
        : base(options, "sslcommerz", PaymentMethod.SslCommerz) { }
}

/// <summary>Sandbox bKash.</summary>
internal sealed class MockBkashGateway : MockPaymentGateway
{
    public MockBkashGateway(IOptions<PaymentOptions> options)
        : base(options, "bkash", PaymentMethod.Bkash) { }
}

/// <summary>Sandbox Nagad.</summary>
internal sealed class MockNagadGateway : MockPaymentGateway
{
    public MockNagadGateway(IOptions<PaymentOptions> options)
        : base(options, "nagad", PaymentMethod.Nagad) { }
}

/// <summary>Picks the gateway registered for a route key; unknown keys fail closed.</summary>
internal sealed class PaymentGatewayResolver : IPaymentGatewayResolver
{
    private readonly IReadOnlyList<IPaymentGateway> _gateways;

    public PaymentGatewayResolver(IEnumerable<IPaymentGateway> gateways) =>
        _gateways = gateways.ToList();

    public IReadOnlyCollection<string> Keys => _gateways.Select(g => g.Key).ToArray();

    public IPaymentGateway? Resolve(string key) => _gateways.FirstOrDefault(
        g => string.Equals(g.Key, key, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Builds callback URLs from the configured client origin only (never from the request).</summary>
internal sealed class PaymentUrlBuilder : IPaymentUrlBuilder
{
    private readonly string _origin;

    public PaymentUrlBuilder(IConfiguration configuration) =>
        _origin = (configuration["Client:Origin"] ?? "http://localhost:4200").TrimEnd('/');

    public PaymentCallbackUrls Build(string gatewayKey) => new(
        $"{_origin}/checkout/success",
        $"{_origin}/checkout/failure",
        $"{_origin}/checkout/cancel",
        $"{_origin}/api/v1/payments/{gatewayKey}/ipn");
}

/// <summary>Fake courier (D-06): booking and tracking succeed locally, no external calls.</summary>
internal sealed class MockCourierService : ICourierService
{
    private readonly CourierOptions _options;
    private readonly ILogger<MockCourierService> _logger;

    public MockCourierService(IOptions<CourierOptions> options, ILogger<MockCourierService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string Provider => _options.Provider;

    public Task<CourierBooking> BookAsync(CourierBookingRequest request, CancellationToken cancellationToken = default)
    {
        var consignmentId = $"MC-{DateTime.UtcNow:yyMMdd}-{Random.Shared.Next(100000, 999999)}";

        _logger.LogInformation(
            "Mock courier booked {OrderNo} for {Recipient} (COD {CodAmount})",
            request.OrderNo,
            request.RecipientPhone,
            request.CodAmount);

        return Task.FromResult(new CourierBooking(consignmentId, $"TRK{consignmentId}", "Booked"));
    }

    public Task<CourierTracking> TrackAsync(string consignmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CourierTracking(
            consignmentId,
            "InTransit",
            new[] { "Booked", "PickedUp", "InTransit" }));
}

/// <summary>SMS sender for v1: writes to the log so OTP flows are testable without an SMS bill.</summary>
internal sealed class LoggingSmsSender : ISmsSender
{
    private readonly ILogger<LoggingSmsSender> _logger;

    public LoggingSmsSender(ILogger<LoggingSmsSender> logger) => _logger = logger;

    public Task SendAsync(string phone, string message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("SMS to {Phone}: {Message}", phone, message);
        return Task.CompletedTask;
    }
}

/// <summary>
/// SMTP sender. With no host configured it logs the message instead, so development and tests do
/// not require a mail server.
/// </summary>
internal sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            _logger.LogInformation("Email (not sent, SMTP unconfigured) to {To}: {Subject}", to, subject);
            return;
        }

        using var message = new MailMessage(_options.From, to, subject, htmlBody) { IsBodyHtml = true };
        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseSsl,
            Credentials = string.IsNullOrWhiteSpace(_options.User)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(_options.User, _options.Password)
        };

        await client.SendMailAsync(message, cancellationToken);
    }
}
