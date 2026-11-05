using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Entities;
using RockyTelecom.Domain.Enums;
using RockyTelecom.Domain.ValueObjects;

namespace RockyTelecom.Application.Features.Forms;

/// <summary>Confirmation returned to the visitor after any public form.</summary>
public sealed record FormReceiptDto(int Id);

/// <summary>
/// Shared rules for the public forms: BD phone format, optional email shape, and a short window in
/// which an identical resubmission (double click, refresh) returns the first record.
/// </summary>
internal static class FormRules
{
    internal static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(10);

    internal static string NormalisePhone(string value) => Phone.Create(value).Normalised;

    internal static string? Clean(string? value, int max)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length <= max ? text : text[..max];
    }
}

// -------------------------------------------------------------------------------------------
// Support ticket
// -------------------------------------------------------------------------------------------

public sealed class SubmitSupportTicketCommand : ICommand<FormReceiptDto>
{
    public string Name { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class SubmitSupportTicketCommandValidator : AbstractValidator<SubmitSupportTicketCommand>
{
    public SubmitSupportTicketCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Phone).Must(Phone.IsValidFormat).WithMessage("Phone must be a Bangladeshi mobile number, e.g. 01712345678.");
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Message).NotEmpty().MaximumLength(2000);
    }
}

public sealed class SubmitSupportTicketCommandHandler : IRequestHandler<SubmitSupportTicketCommand, FormReceiptDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public SubmitSupportTicketCommandHandler(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<FormReceiptDto> Handle(SubmitSupportTicketCommand request, CancellationToken cancellationToken)
    {
        var phone = FormRules.NormalisePhone(request.Phone);
        var subject = request.Subject.Trim();
        var message = request.Message.Trim();
        var now = _clock.UtcNow;
        var since = now - FormRules.DuplicateWindow;

        var existing = await _db.SupportTickets
            .Where(t => t.Phone == phone && t.Subject == subject && t.Message == message && t.CreatedAt >= since)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing.HasValue)
        {
            return new FormReceiptDto(existing.Value);
        }

        var ticket = new SupportTicket
        {
            Name = request.Name.Trim(),
            Phone = phone,
            Email = FormRules.Clean(request.Email, 256),
            Subject = subject,
            Message = message,
            Status = SupportTicketStatus.Open,
            CreatedAt = now
        };

        _db.SupportTickets.Add(ticket);
        await _db.SaveChangesAsync(cancellationToken);
        return new FormReceiptDto(ticket.Id);
    }
}

// -------------------------------------------------------------------------------------------
// Trade-in request
// -------------------------------------------------------------------------------------------

public sealed class SubmitTradeInCommand : ICommand<FormReceiptDto>
{
    public string Name { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string DeviceModel { get; init; } = string.Empty;
    public string? Condition { get; init; }
    public string? Note { get; init; }
}

public sealed class SubmitTradeInCommandValidator : AbstractValidator<SubmitTradeInCommand>
{
    public SubmitTradeInCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Phone).Must(Phone.IsValidFormat).WithMessage("Phone must be a Bangladeshi mobile number, e.g. 01712345678.");
        RuleFor(x => x.DeviceModel).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Condition).MaximumLength(60);
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

public sealed class SubmitTradeInCommandHandler : IRequestHandler<SubmitTradeInCommand, FormReceiptDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public SubmitTradeInCommandHandler(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<FormReceiptDto> Handle(SubmitTradeInCommand request, CancellationToken cancellationToken)
    {
        var phone = FormRules.NormalisePhone(request.Phone);
        var model = request.DeviceModel.Trim();
        var now = _clock.UtcNow;
        var since = now - FormRules.DuplicateWindow;

        var existing = await _db.TradeInRequests
            .Where(t => t.Phone == phone && t.DeviceModel == model && t.CreatedAt >= since)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing.HasValue)
        {
            return new FormReceiptDto(existing.Value);
        }

        var entry = new TradeInRequest
        {
            Name = request.Name.Trim(),
            Phone = phone,
            DeviceModel = model,
            Condition = FormRules.Clean(request.Condition, 60),
            Note = FormRules.Clean(request.Note, 500),
            Status = TradeInStatus.Submitted,
            CreatedAt = now
        };

        _db.TradeInRequests.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);
        return new FormReceiptDto(entry.Id);
    }
}

// -------------------------------------------------------------------------------------------
// Corporate inquiry
// -------------------------------------------------------------------------------------------

public sealed class SubmitCorporateInquiryCommand : ICommand<FormReceiptDto>
{
    public string CompanyName { get; init; } = string.Empty;
    public string ContactName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class SubmitCorporateInquiryCommandValidator : AbstractValidator<SubmitCorporateInquiryCommand>
{
    public SubmitCorporateInquiryCommandValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(160);
        RuleFor(x => x.ContactName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Phone).Must(Phone.IsValidFormat).WithMessage("Phone must be a Bangladeshi mobile number, e.g. 01712345678.");
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Message).NotEmpty().MaximumLength(2000);
    }
}

public sealed class SubmitCorporateInquiryCommandHandler : IRequestHandler<SubmitCorporateInquiryCommand, FormReceiptDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public SubmitCorporateInquiryCommandHandler(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<FormReceiptDto> Handle(SubmitCorporateInquiryCommand request, CancellationToken cancellationToken)
    {
        var phone = FormRules.NormalisePhone(request.Phone);
        var company = request.CompanyName.Trim();
        var message = request.Message.Trim();
        var now = _clock.UtcNow;
        var since = now - FormRules.DuplicateWindow;

        var existing = await _db.CorporateInquiries
            .Where(i => i.Phone == phone && i.CompanyName == company && i.Message == message && i.CreatedAt >= since)
            .Select(i => (int?)i.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing.HasValue)
        {
            return new FormReceiptDto(existing.Value);
        }

        var entry = new CorporateInquiry
        {
            CompanyName = company,
            ContactName = request.ContactName.Trim(),
            Phone = phone,
            Email = FormRules.Clean(request.Email, 256),
            Message = message,
            Status = CorporateInquiryStatus.New,
            CreatedAt = now
        };

        _db.CorporateInquiries.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);
        return new FormReceiptDto(entry.Id);
    }
}

// -------------------------------------------------------------------------------------------
// Feedback
// -------------------------------------------------------------------------------------------

public sealed class SubmitFeedbackCommand : ICommand<FormReceiptDto>
{
    public string? Name { get; init; }
    public string? Email { get; init; }
    public int? Rating { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class SubmitFeedbackCommandValidator : AbstractValidator<SubmitFeedbackCommand>
{
    public SubmitFeedbackCommandValidator()
    {
        RuleFor(x => x.Name).MaximumLength(120);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(254).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Rating).InclusiveBetween(1, 5).When(x => x.Rating.HasValue);
        RuleFor(x => x.Message).NotEmpty().MaximumLength(2000);
    }
}

public sealed class SubmitFeedbackCommandHandler : IRequestHandler<SubmitFeedbackCommand, FormReceiptDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public SubmitFeedbackCommandHandler(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<FormReceiptDto> Handle(SubmitFeedbackCommand request, CancellationToken cancellationToken)
    {
        var message = request.Message.Trim();
        var now = _clock.UtcNow;
        var since = now - FormRules.DuplicateWindow;
        var email = FormRules.Clean(request.Email, 254);

        var existing = await _db.FeedbackEntries
            .Where(f => f.Message == message && f.Email == email && f.CreatedAt >= since)
            .Select(f => (int?)f.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing.HasValue)
        {
            return new FormReceiptDto(existing.Value);
        }

        var entry = new FeedbackEntry
        {
            Name = FormRules.Clean(request.Name, 120),
            Email = email,
            Rating = request.Rating,
            Message = message,
            CreatedAt = now
        };

        _db.FeedbackEntries.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);
        return new FormReceiptDto(entry.Id);
    }
}

// -------------------------------------------------------------------------------------------
// Newsletter
// -------------------------------------------------------------------------------------------

public sealed class SubscribeNewsletterCommand : ICommand<FormReceiptDto>
{
    public string Email { get; init; } = string.Empty;
}

public sealed class SubscribeNewsletterCommandValidator : AbstractValidator<SubscribeNewsletterCommand>
{
    public SubscribeNewsletterCommandValidator() =>
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
}

public sealed class SubscribeNewsletterCommandHandler : IRequestHandler<SubscribeNewsletterCommand, FormReceiptDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public SubscribeNewsletterCommandHandler(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>Subscribing twice is not an error: the visitor gets the same friendly answer.</summary>
    public async Task<FormReceiptDto> Handle(SubscribeNewsletterCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var existing = await _db.NewsletterSubscribers.FirstOrDefaultAsync(s => s.Email == email, cancellationToken);
        if (existing is not null)
        {
            existing.IsActive = true;
            await _db.SaveChangesAsync(cancellationToken);
            return new FormReceiptDto(existing.Id);
        }

        var subscriber = new NewsletterSubscriber { Email = email, CreatedAt = _clock.UtcNow, IsActive = true };
        _db.NewsletterSubscribers.Add(subscriber);
        await _db.SaveChangesAsync(cancellationToken);
        return new FormReceiptDto(subscriber.Id);
    }
}

// -------------------------------------------------------------------------------------------
// Admin inbox: one list for every kind of submission
// -------------------------------------------------------------------------------------------

public static class InboxKinds
{
    public const string Support = "support";
    public const string TradeIn = "trade-in";
    public const string Corporate = "corporate";
    public const string Feedback = "feedback";
    public const string Newsletter = "newsletter";

    public static readonly IReadOnlyList<string> All = new[] { Support, TradeIn, Corporate, Feedback, Newsletter };

    /// <summary>Statuses an admin can move a submission to, per kind (newsletter has none).</summary>
    public static IReadOnlyList<string> Statuses(string kind) => kind switch
    {
        Support => Enum.GetNames<SupportTicketStatus>(),
        TradeIn => Enum.GetNames<TradeInStatus>(),
        Corporate => Enum.GetNames<CorporateInquiryStatus>(),
        Feedback => new[] { "New", "Reviewed" },
        _ => Array.Empty<string>()
    };
}

public sealed record InboxItemDto(
    int Id,
    string Kind,
    string Name,
    string? Phone,
    string? Email,
    string? Subject,
    string? Message,
    string Status,
    DateTime CreatedAt);

public sealed record InboxPageDto(
    string Kind,
    IReadOnlyList<string> Statuses,
    IReadOnlyList<InboxItemDto> Items,
    int Total,
    IReadOnlyDictionary<string, int> Counts);

public sealed class ListInboxQuery : IQuery<InboxPageDto>
{
    public string Kind { get; init; } = InboxKinds.Support;
    public int Take { get; init; } = 50;
}

public sealed class ListInboxQueryHandler : IRequestHandler<ListInboxQuery, InboxPageDto>
{
    private readonly IApplicationDbContext _db;

    public ListInboxQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<InboxPageDto> Handle(ListInboxQuery request, CancellationToken cancellationToken)
    {
        var kind = request.Kind.Trim().ToLowerInvariant();
        var take = Math.Clamp(request.Take, 1, 200);

        if (!InboxKinds.All.Contains(kind))
        {
            throw new DomainValidationException("Unknown inbox kind.", new Dictionary<string, string[]> { ["kind"] = new[] { "Use one of: " + string.Join(", ", InboxKinds.All) } });
        }

        // Small badge counts for the tab bar, so staff see what needs attention at a glance.
        var counts = new Dictionary<string, int>
        {
            [InboxKinds.Support] = await _db.SupportTickets.CountAsync(t => t.Status == SupportTicketStatus.Open, cancellationToken),
            [InboxKinds.TradeIn] = await _db.TradeInRequests.CountAsync(t => t.Status == TradeInStatus.Submitted, cancellationToken),
            [InboxKinds.Corporate] = await _db.CorporateInquiries.CountAsync(i => i.Status == CorporateInquiryStatus.New, cancellationToken),
            [InboxKinds.Feedback] = await _db.FeedbackEntries.CountAsync(f => !f.IsReviewed, cancellationToken),
            [InboxKinds.Newsletter] = await _db.NewsletterSubscribers.CountAsync(s => s.IsActive, cancellationToken)
        };

        IReadOnlyList<InboxItemDto> items;
        int total;

        switch (kind)
        {
            case InboxKinds.Support:
            {
                total = await _db.SupportTickets.CountAsync(cancellationToken);
                var rows = await _db.SupportTickets.AsNoTracking().OrderByDescending(t => t.CreatedAt).Take(take).ToListAsync(cancellationToken);
                items = rows.Select(t => new InboxItemDto(t.Id, kind, t.Name, t.Phone, t.Email, t.Subject, t.Message, t.Status.ToString(), t.CreatedAt)).ToList();
                break;
            }
            case InboxKinds.TradeIn:
            {
                total = await _db.TradeInRequests.CountAsync(cancellationToken);
                var rows = await _db.TradeInRequests.AsNoTracking().OrderByDescending(t => t.CreatedAt).Take(take).ToListAsync(cancellationToken);
                items = rows.Select(t => new InboxItemDto(
                    t.Id, kind, t.Name, t.Phone, null, t.DeviceModel,
                    string.Join(" — ", new[] { t.Condition, t.Note }.Where(x => !string.IsNullOrWhiteSpace(x))),
                    t.Status.ToString(), t.CreatedAt)).ToList();
                break;
            }
            case InboxKinds.Corporate:
            {
                total = await _db.CorporateInquiries.CountAsync(cancellationToken);
                var rows = await _db.CorporateInquiries.AsNoTracking().OrderByDescending(i => i.CreatedAt).Take(take).ToListAsync(cancellationToken);
                items = rows.Select(i => new InboxItemDto(i.Id, kind, i.ContactName, i.Phone, i.Email, i.CompanyName, i.Message, i.Status.ToString(), i.CreatedAt)).ToList();
                break;
            }
            case InboxKinds.Feedback:
            {
                total = await _db.FeedbackEntries.CountAsync(cancellationToken);
                var rows = await _db.FeedbackEntries.AsNoTracking().OrderByDescending(f => f.CreatedAt).Take(take).ToListAsync(cancellationToken);
                items = rows.Select(f => new InboxItemDto(
                    f.Id, kind, f.Name ?? "Anonymous", null, f.Email, f.Rating.HasValue ? $"{f.Rating} / 5" : null, f.Message,
                    f.IsReviewed ? "Reviewed" : "New", f.CreatedAt)).ToList();
                break;
            }
            default:
            {
                total = await _db.NewsletterSubscribers.CountAsync(cancellationToken);
                var rows = await _db.NewsletterSubscribers.AsNoTracking().OrderByDescending(s => s.CreatedAt).Take(take).ToListAsync(cancellationToken);
                items = rows.Select(s => new InboxItemDto(s.Id, kind, s.Email, null, s.Email, null, null, s.IsActive ? "Subscribed" : "Unsubscribed", s.CreatedAt)).ToList();
                break;
            }
        }

        return new InboxPageDto(kind, InboxKinds.Statuses(kind), items, total, counts);
    }
}

public sealed class ChangeInboxStatusCommand : ICommand<Unit>
{
    public string Kind { get; set; } = string.Empty;
    public int Id { get; set; }
    public string Status { get; init; } = string.Empty;
}

public sealed class ChangeInboxStatusCommandValidator : AbstractValidator<ChangeInboxStatusCommand>
{
    public ChangeInboxStatusCommandValidator()
    {
        RuleFor(x => x.Kind).Must(k => InboxKinds.All.Contains(k?.ToLowerInvariant() ?? string.Empty)).WithMessage("Unknown inbox kind.");
        RuleFor(x => x.Status).NotEmpty();
    }
}

public sealed class ChangeInboxStatusCommandHandler : IRequestHandler<ChangeInboxStatusCommand, Unit>
{
    private readonly IApplicationDbContext _db;

    public ChangeInboxStatusCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<Unit> Handle(ChangeInboxStatusCommand request, CancellationToken cancellationToken)
    {
        var kind = request.Kind.Trim().ToLowerInvariant();

        TStatus Parse<TStatus>() where TStatus : struct, Enum =>
            Enum.TryParse<TStatus>(request.Status, true, out var value) && Enum.IsDefined(value)
                ? value
                : throw new DomainValidationException("Unknown status.", new Dictionary<string, string[]> { ["status"] = new[] { "Use one of: " + string.Join(", ", InboxKinds.Statuses(kind)) } });

        switch (kind)
        {
            case InboxKinds.Support:
            {
                var item = await _db.SupportTickets.FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken) ?? throw new NotFoundException("Support ticket", request.Id);
                item.Status = Parse<SupportTicketStatus>();
                break;
            }
            case InboxKinds.TradeIn:
            {
                var item = await _db.TradeInRequests.FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken) ?? throw new NotFoundException("Trade-in request", request.Id);
                item.Status = Parse<TradeInStatus>();
                break;
            }
            case InboxKinds.Corporate:
            {
                var item = await _db.CorporateInquiries.FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken) ?? throw new NotFoundException("Corporate inquiry", request.Id);
                item.Status = Parse<CorporateInquiryStatus>();
                break;
            }
            case InboxKinds.Feedback:
            {
                var item = await _db.FeedbackEntries.FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken) ?? throw new NotFoundException("Feedback", request.Id);
                item.IsReviewed = string.Equals(request.Status, "Reviewed", StringComparison.OrdinalIgnoreCase);
                break;
            }
            default:
                throw new DomainValidationException("Newsletter subscribers have no status to change.");
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
