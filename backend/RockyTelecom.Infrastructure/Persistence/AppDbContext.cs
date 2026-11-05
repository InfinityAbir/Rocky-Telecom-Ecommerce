using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Domain.Entities;
using RockyTelecom.Infrastructure.Identity;

namespace RockyTelecom.Infrastructure.Persistence;

/// <summary>
/// Single EF Core context: Identity tables plus the PRD §5 domain model. Entity rules live in
/// <c>Persistence/Configurations</c> and are picked up by assembly scanning.
/// </summary>
public class AppDbContext : IdentityDbContext<AppUser>, IApplicationDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<SpecTemplate> SpecTemplates => Set<SpecTemplate>();
    public DbSet<SpecGroup> SpecGroups => Set<SpecGroup>();
    public DbSet<SpecField> SpecFields => Set<SpecField>();
    public DbSet<ProductSpec> ProductSpecs => Set<ProductSpec>();
    public DbSet<VariantOption> VariantOptions => Set<VariantOption>();

    public DbSet<FlashSale> FlashSales => Set<FlashSale>();
    public DbSet<FlashSaleItem> FlashSaleItems => Set<FlashSaleItem>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<Banner> Banners => Set<Banner>();
    public DbSet<EmiPlan> EmiPlans => Set<EmiPlan>();

    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Wishlist> Wishlists => Set<Wishlist>();
    public DbSet<Review> Reviews => Set<Review>();

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();

    public DbSet<StoreInfo> StoreInfos => Set<StoreInfo>();
    public DbSet<DeliveryZone> DeliveryZones => Set<DeliveryZone>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<SlugRedirect> SlugRedirects => Set<SlugRedirect>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();
    public DbSet<NewsItem> NewsItems => Set<NewsItem>();
    public DbSet<Showcase> Showcases => Set<Showcase>();
    public DbSet<ShowcaseItem> ShowcaseItems => Set<ShowcaseItem>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<NewsletterSubscriber> NewsletterSubscribers => Set<NewsletterSubscriber>();
    public DbSet<FeedbackEntry> FeedbackEntries => Set<FeedbackEntry>();
    public DbSet<CampaignItem> CampaignItems => Set<CampaignItem>();
    public DbSet<PreOrder> PreOrders => Set<PreOrder>();
    public DbSet<TradeInRequest> TradeInRequests => Set<TradeInRequest>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<CorporateInquiry> CorporateInquiries => Set<CorporateInquiry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        builder.Entity<AppUser>(user =>
        {
            user.Property(u => u.Name).HasMaxLength(120).IsRequired();
            // Phone is the login id, so it must be unique across accounts (FR-06).
            user.HasIndex(u => u.PhoneNumber).IsUnique().HasFilter("[PhoneNumber] IS NOT NULL");
        });
    }
}
