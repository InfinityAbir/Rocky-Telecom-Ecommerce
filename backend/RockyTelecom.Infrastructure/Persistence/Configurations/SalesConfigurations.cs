using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RockyTelecom.Domain.Entities;

namespace RockyTelecom.Infrastructure.Persistence.Configurations;

public sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.Property(c => c.UserId).HasMaxLength(450);
        builder.Property(c => c.GuestToken).HasMaxLength(64);

        builder.HasIndex(c => c.UserId).IsUnique().HasFilter("[UserId] IS NOT NULL");
        builder.HasIndex(c => c.GuestToken).IsUnique().HasFilter("[GuestToken] IS NOT NULL");
    }
}

public sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.HasIndex(i => new { i.CartId, i.VariantId }).IsUnique();

        builder.HasOne(i => i.Cart)
            .WithMany(c => c.Items)
            .HasForeignKey(i => i.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Variant)
            .WithMany()
            .HasForeignKey(i => i.VariantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WishlistConfiguration : IEntityTypeConfiguration<Wishlist>
{
    public void Configure(EntityTypeBuilder<Wishlist> builder)
    {
        builder.Property(w => w.UserId).HasMaxLength(450).IsRequired();

        builder.HasIndex(w => new { w.UserId, w.ProductId }).IsUnique();

        builder.HasOne(w => w.Product)
            .WithMany()
            .HasForeignKey(w => w.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.Property(r => r.UserId).HasMaxLength(450).IsRequired();
        builder.Property(r => r.Body).HasMaxLength(2000);

        builder.HasIndex(r => new { r.ProductId, r.UserId }).IsUnique();
        builder.HasIndex(r => new { r.IsApproved, r.ProductId });

        builder.HasOne(r => r.Product)
            .WithMany(p => p.Reviews)
            .HasForeignKey(r => r.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.Property(c => c.Code).HasMaxLength(40).IsRequired();
        builder.Property(c => c.Value).HasPrecision(18, 2);
        builder.Property(c => c.MinCart).HasPrecision(18, 2);
        builder.Property(c => c.MaxDiscount).HasPrecision(18, 2);

        builder.HasIndex(c => c.Code).IsUnique();
    }
}

public sealed class FlashSaleConfiguration : IEntityTypeConfiguration<FlashSale>
{
    public void Configure(EntityTypeBuilder<FlashSale> builder)
    {
        builder.Property(f => f.Title).HasMaxLength(140).IsRequired();
        builder.HasIndex(f => new { f.IsActive, f.StartAt, f.EndAt });
    }
}

public sealed class FlashSaleItemConfiguration : IEntityTypeConfiguration<FlashSaleItem>
{
    public void Configure(EntityTypeBuilder<FlashSaleItem> builder)
    {
        builder.Property(i => i.SalePrice).HasPrecision(18, 2);

        builder.HasIndex(i => new { i.FlashSaleId, i.VariantId }).IsUnique();

        builder.HasOne(i => i.FlashSale)
            .WithMany(f => f.Items)
            .HasForeignKey(i => i.FlashSaleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Variant)
            .WithMany()
            .HasForeignKey(i => i.VariantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.Property(o => o.OrderNo).HasMaxLength(20).IsRequired();
        builder.Property(o => o.UserId).HasMaxLength(450);
        builder.Property(o => o.GuestPhone).HasMaxLength(20);
        builder.Property(o => o.AddressSnapshot).HasColumnType("nvarchar(max)");
        builder.Property(o => o.Note).HasMaxLength(500);
        builder.Property(o => o.IdempotencyKey).HasMaxLength(80);

        builder.Property(o => o.SubTotal).HasPrecision(18, 2);
        builder.Property(o => o.Discount).HasPrecision(18, 2);
        builder.Property(o => o.DeliveryFee).HasPrecision(18, 2);
        builder.Property(o => o.Total).HasPrecision(18, 2);

        builder.HasIndex(o => o.OrderNo).IsUnique();
        // POST /orders is safe to retry: the same key can never create a second order (FR-08).
        builder.HasIndex(o => o.IdempotencyKey).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
        builder.HasIndex(o => o.Status);
        builder.HasIndex(o => o.UserId);

        builder.HasOne(o => o.Coupon)
            .WithMany()
            .HasForeignKey(o => o.CouponId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.Property(i => i.NameSnapshot).HasMaxLength(300).IsRequired();
        builder.Property(i => i.PriceSnapshot).HasPrecision(18, 2);

        builder.HasIndex(i => i.OrderId);
        builder.HasIndex(i => i.VariantId);

        builder.HasOne(i => i.Order)
            .WithMany(o => o.Items)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(i => i.VariantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.Property(h => h.ChangedBy).HasMaxLength(450);
        builder.Property(h => h.Note).HasMaxLength(500);

        builder.HasIndex(h => h.OrderId);

        builder.HasOne(h => h.Order)
            .WithMany(o => o.StatusHistory)
            .HasForeignKey(h => h.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(p => p.Gateway).HasMaxLength(40).IsRequired();
        builder.Property(p => p.TrxId).HasMaxLength(120);
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.RawResponse).HasColumnType("nvarchar(max)");

        builder.HasIndex(p => p.OrderId);
        // An IPN may be replayed; the gateway transaction id is the idempotency anchor (FR-10).
        builder.HasIndex(p => new { p.Gateway, p.TrxId }).IsUnique().HasFilter("[TrxId] IS NOT NULL");

        builder.HasOne(p => p.Order)
            .WithMany(o => o.Payments)
            .HasForeignKey(p => p.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.HasIndex(r => r.OrderId);
        builder.HasIndex(r => new { r.IsReleased, r.ExpiresAt });

        builder.HasOne(r => r.Variant)
            .WithMany()
            .HasForeignKey(r => r.VariantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
