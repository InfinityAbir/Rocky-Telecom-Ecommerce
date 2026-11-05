using Microsoft.EntityFrameworkCore;
using RockyTelecom.Domain.Entities;

namespace RockyTelecom.Application.Common.Abstractions;

/// <summary>
/// Persistence surface for use cases. Handlers never touch the concrete DbContext;
/// Infrastructure supplies the EF Core implementation with per-entity configurations.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Category> Categories { get; }
    DbSet<Brand> Brands { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductVariant> ProductVariants { get; }
    DbSet<ProductImage> ProductImages { get; }
    DbSet<SpecTemplate> SpecTemplates { get; }
    DbSet<SpecGroup> SpecGroups { get; }
    DbSet<SpecField> SpecFields { get; }
    DbSet<ProductSpec> ProductSpecs { get; }
    DbSet<VariantOption> VariantOptions { get; }

    DbSet<FlashSale> FlashSales { get; }
    DbSet<FlashSaleItem> FlashSaleItems { get; }
    DbSet<Coupon> Coupons { get; }
    DbSet<Banner> Banners { get; }
    DbSet<EmiPlan> EmiPlans { get; }

    DbSet<Address> Addresses { get; }
    DbSet<Cart> Carts { get; }
    DbSet<CartItem> CartItems { get; }
    DbSet<Wishlist> Wishlists { get; }
    DbSet<Review> Reviews { get; }

    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<OrderStatusHistory> OrderStatusHistories { get; }
    DbSet<Payment> Payments { get; }
    DbSet<StockReservation> StockReservations { get; }

    DbSet<StoreInfo> StoreInfos { get; }
    DbSet<DeliveryZone> DeliveryZones { get; }
    DbSet<Shipment> Shipments { get; }
    DbSet<SlugRedirect> SlugRedirects { get; }
    DbSet<AuditLog> AuditLogs { get; }

    DbSet<BlogPost> BlogPosts { get; }
    DbSet<NewsItem> NewsItems { get; }
    DbSet<Showcase> Showcases { get; }
    DbSet<ShowcaseItem> ShowcaseItems { get; }
    DbSet<Campaign> Campaigns { get; }
    DbSet<StockAdjustment> StockAdjustments { get; }
    DbSet<NewsletterSubscriber> NewsletterSubscribers { get; }
    DbSet<FeedbackEntry> FeedbackEntries { get; }
    DbSet<CampaignItem> CampaignItems { get; }
    DbSet<PreOrder> PreOrders { get; }
    DbSet<TradeInRequest> TradeInRequests { get; }
    DbSet<SupportTicket> SupportTickets { get; }
    DbSet<CorporateInquiry> CorporateInquiries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
