using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Enums;

namespace RockyTelecom.Domain.Entities;

public class Banner : AuditableEntity
{
    /// <summary>Placement slot, e.g. <c>home.hero</c>, <c>home.promo</c>, <c>home.strip</c>.</summary>
    public string Placement { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? Link { get; set; }
    public int SortOrder { get; set; }
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public bool IsActive { get; set; } = true;

    public bool IsLiveAt(DateTime utcNow) =>
        IsActive
        && (!StartAt.HasValue || utcNow >= StartAt.Value)
        && (!EndAt.HasValue || utcNow <= EndAt.Value);
}

public class BlogPost : AuditableEntity
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Cover { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? Excerpt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public bool IsPublished { get; set; }
    public string? SeoTitle { get; set; }
    public string? SeoDesc { get; set; }
}

public class PreOrder : BaseEntity
{
    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Note { get; set; }
    public PreOrderStatus Status { get; set; } = PreOrderStatus.Pending;
    public DateTime CreatedAt { get; set; }
}

public class TradeInRequest : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string DeviceModel { get; set; } = string.Empty;
    public string? Condition { get; set; }
    public string? Note { get; set; }
    public TradeInStatus Status { get; set; } = TradeInStatus.Submitted;
    public DateTime CreatedAt { get; set; }
}

public class SupportTicket : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public SupportTicketStatus Status { get; set; } = SupportTicketStatus.Open;
    public DateTime CreatedAt { get; set; }
}

public class CorporateInquiry : BaseEntity
{
    public string CompanyName { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Message { get; set; } = string.Empty;
    public CorporateInquiryStatus Status { get; set; } = CorporateInquiryStatus.New;
    public DateTime CreatedAt { get; set; }
}

/// <summary>One line of the scrolling announcement ticker shown above the header.</summary>
public class NewsItem : AuditableEntity
{
    public string Title { get; set; } = string.Empty;
    public string? LinkUrl { get; set; }
    public bool OpenInNewTab { get; set; }
    public int SortOrder { get; set; }
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// A hand-picked, ordered shelf of products on the home page (e.g. "Trending now").
/// Distinct from the automatic shelves (featured / new arrivals / best deals).
/// </summary>
public class Showcase : AuditableEntity
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<ShowcaseItem> Items { get; set; } = new List<ShowcaseItem>();
}

public class ShowcaseItem : BaseEntity
{
    public int ShowcaseId { get; set; }
    public Showcase? Showcase { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>
/// A time-boxed offer page (e.g. "Eid sale") with its own banner, text and hand-picked products,
/// served at /offer/{slug}. Visible only while active and inside its window.
/// </summary>
public class Campaign : AuditableEntity
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    /// <summary>Plain text; shown as paragraphs. No HTML is stored or rendered.</summary>
    public string? Description { get; set; }
    public string? BannerUrl { get; set; }
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<CampaignItem> Items { get; set; } = new List<CampaignItem>();

    public bool IsLiveAt(DateTime utcNow) =>
        IsActive
        && (!StartAt.HasValue || utcNow >= StartAt.Value)
        && (!EndAt.HasValue || utcNow <= EndAt.Value);
}

public class CampaignItem : BaseEntity
{
    public int CampaignId { get; set; }
    public Campaign? Campaign { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>A visitor who asked for shop news by email. One row per address.</summary>
public class NewsletterSubscriber : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Free-form feedback about the shop or the website, with an optional star rating.</summary>
public class FeedbackEntry : BaseEntity
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    /// <summary>1–5 when given.</summary>
    public int? Rating { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsReviewed { get; set; }
    public DateTime CreatedAt { get; set; }
}
