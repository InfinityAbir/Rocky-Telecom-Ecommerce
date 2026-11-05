using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RockyTelecom.Domain.Entities;

namespace RockyTelecom.Infrastructure.Persistence.Configurations;

public sealed class BannerConfiguration : IEntityTypeConfiguration<Banner>
{
    public void Configure(EntityTypeBuilder<Banner> builder)
    {
        builder.Property(b => b.Placement).HasMaxLength(40).IsRequired();
        builder.Property(b => b.Title).HasMaxLength(140);
        builder.Property(b => b.ImageUrl).HasMaxLength(512).IsRequired();
        builder.Property(b => b.Link).HasMaxLength(512);

        builder.HasIndex(b => new { b.Placement, b.IsActive, b.SortOrder });
    }
}

public sealed class BlogPostConfiguration : IEntityTypeConfiguration<BlogPost>
{
    public void Configure(EntityTypeBuilder<BlogPost> builder)
    {
        builder.Property(p => p.Title).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Slug).HasMaxLength(220).IsRequired();
        builder.Property(p => p.Cover).HasMaxLength(512);
        builder.Property(p => p.Excerpt).HasMaxLength(300);
        builder.Property(p => p.SeoTitle).HasMaxLength(70);
        builder.Property(p => p.SeoDesc).HasMaxLength(160);

        builder.HasIndex(p => p.Slug).IsUnique();
    }
}

public sealed class PreOrderConfiguration : IEntityTypeConfiguration<PreOrder>
{
    public void Configure(EntityTypeBuilder<PreOrder> builder)
    {
        builder.Property(p => p.Name).HasMaxLength(120).IsRequired();
        builder.Property(p => p.Phone).HasMaxLength(20).IsRequired();
        builder.Property(p => p.Note).HasMaxLength(500);

        builder.HasIndex(p => p.Status);

        builder.HasOne(p => p.Product)
            .WithMany()
            .HasForeignKey(p => p.ProductId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class TradeInRequestConfiguration : IEntityTypeConfiguration<TradeInRequest>
{
    public void Configure(EntityTypeBuilder<TradeInRequest> builder)
    {
        builder.Property(t => t.Name).HasMaxLength(120).IsRequired();
        builder.Property(t => t.Phone).HasMaxLength(20).IsRequired();
        builder.Property(t => t.DeviceModel).HasMaxLength(160).IsRequired();
        builder.Property(t => t.Condition).HasMaxLength(60);
        builder.Property(t => t.Note).HasMaxLength(500);

        builder.HasIndex(t => t.Status);
    }
}

public sealed class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> builder)
    {
        builder.Property(t => t.Name).HasMaxLength(120).IsRequired();
        builder.Property(t => t.Phone).HasMaxLength(20).IsRequired();
        builder.Property(t => t.Email).HasMaxLength(256);
        builder.Property(t => t.Subject).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Message).HasMaxLength(2000).IsRequired();

        builder.HasIndex(t => t.Status);
    }
}

public sealed class CorporateInquiryConfiguration : IEntityTypeConfiguration<CorporateInquiry>
{
    public void Configure(EntityTypeBuilder<CorporateInquiry> builder)
    {
        builder.Property(i => i.CompanyName).HasMaxLength(160).IsRequired();
        builder.Property(i => i.ContactName).HasMaxLength(120).IsRequired();
        builder.Property(i => i.Phone).HasMaxLength(20).IsRequired();
        builder.Property(i => i.Email).HasMaxLength(256);
        builder.Property(i => i.Message).HasMaxLength(2000).IsRequired();

        builder.HasIndex(i => i.Status);
    }
}

public sealed class NewsItemConfiguration : IEntityTypeConfiguration<NewsItem>
{
    public void Configure(EntityTypeBuilder<NewsItem> builder)
    {
        builder.Property(n => n.Title).HasMaxLength(200).IsRequired();
        builder.Property(n => n.LinkUrl).HasMaxLength(512);

        builder.HasIndex(n => new { n.IsActive, n.SortOrder });
    }
}

public sealed class ShowcaseConfiguration : IEntityTypeConfiguration<Showcase>
{
    public void Configure(EntityTypeBuilder<Showcase> builder)
    {
        builder.Property(s => s.Slug).HasMaxLength(80).IsRequired();
        builder.Property(s => s.Title).HasMaxLength(120).IsRequired();
        builder.Property(s => s.Subtitle).HasMaxLength(200);

        builder.HasIndex(s => s.Slug).IsUnique();
        builder.HasIndex(s => new { s.IsActive, s.SortOrder });

        builder.HasMany(s => s.Items)
            .WithOne(i => i.Showcase)
            .HasForeignKey(i => i.ShowcaseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ShowcaseItemConfiguration : IEntityTypeConfiguration<ShowcaseItem>
{
    public void Configure(EntityTypeBuilder<ShowcaseItem> builder)
    {
        builder.HasIndex(i => new { i.ShowcaseId, i.ProductId }).IsUnique();

        // NoAction: a product delete must not silently shrink a shelf through a second cascade path.
        builder.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.Property(c => c.Slug).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Title).HasMaxLength(140).IsRequired();
        builder.Property(c => c.Subtitle).HasMaxLength(200);
        builder.Property(c => c.Description).HasMaxLength(4000);
        builder.Property(c => c.BannerUrl).HasMaxLength(512);

        builder.HasIndex(c => c.Slug).IsUnique();
        builder.HasIndex(c => new { c.IsActive, c.SortOrder });

        builder.HasMany(c => c.Items)
            .WithOne(i => i.Campaign)
            .HasForeignKey(i => i.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CampaignItemConfiguration : IEntityTypeConfiguration<CampaignItem>
{
    public void Configure(EntityTypeBuilder<CampaignItem> builder)
    {
        builder.HasIndex(i => new { i.CampaignId, i.ProductId }).IsUnique();

        builder.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class NewsletterSubscriberConfiguration : IEntityTypeConfiguration<NewsletterSubscriber>
{
    public void Configure(EntityTypeBuilder<NewsletterSubscriber> builder)
    {
        builder.Property(s => s.Email).HasMaxLength(254).IsRequired();
        builder.HasIndex(s => s.Email).IsUnique();
    }
}

public sealed class FeedbackEntryConfiguration : IEntityTypeConfiguration<FeedbackEntry>
{
    public void Configure(EntityTypeBuilder<FeedbackEntry> builder)
    {
        builder.Property(f => f.Name).HasMaxLength(120);
        builder.Property(f => f.Email).HasMaxLength(254);
        builder.Property(f => f.Message).HasMaxLength(2000).IsRequired();
        builder.HasIndex(f => f.CreatedAt);
    }
}
