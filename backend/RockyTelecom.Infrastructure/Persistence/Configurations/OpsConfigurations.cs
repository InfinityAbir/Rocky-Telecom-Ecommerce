using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RockyTelecom.Domain.Entities;

namespace RockyTelecom.Infrastructure.Persistence.Configurations;

public sealed class StoreInfoConfiguration : IEntityTypeConfiguration<StoreInfo>
{
    public void Configure(EntityTypeBuilder<StoreInfo> builder)
    {
        builder.Property(s => s.Name).HasMaxLength(160).IsRequired();
        builder.Property(s => s.Address).HasMaxLength(300).IsRequired();
        builder.Property(s => s.Phone).HasMaxLength(20).IsRequired();
        builder.Property(s => s.Email).HasMaxLength(256);
        builder.Property(s => s.OpeningHours).HasMaxLength(120);
        builder.Property(s => s.MapLat).HasPrecision(9, 6);
        builder.Property(s => s.MapLng).HasPrecision(9, 6);
    }
}

public sealed class DeliveryZoneConfiguration : IEntityTypeConfiguration<DeliveryZone>
{
    public void Configure(EntityTypeBuilder<DeliveryZone> builder)
    {
        builder.Property(z => z.Name).HasMaxLength(80).IsRequired();
        builder.Property(z => z.Districts).HasMaxLength(1000).IsRequired();
        builder.Property(z => z.Charge).HasPrecision(18, 2);

        builder.HasIndex(z => z.IsActive);
    }
}

public sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.Property(s => s.Courier).HasMaxLength(60).IsRequired();
        builder.Property(s => s.ConsignmentId).HasMaxLength(120);
        builder.Property(s => s.TrackingCode).HasMaxLength(120);
        builder.Property(s => s.Status).HasMaxLength(60);
        builder.Property(s => s.Charge).HasPrecision(18, 2);

        builder.HasIndex(s => s.OrderId);

        builder.HasOne(s => s.Order)
            .WithMany()
            .HasForeignKey(s => s.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class EmiPlanConfiguration : IEntityTypeConfiguration<EmiPlan>
{
    public void Configure(EntityTypeBuilder<EmiPlan> builder)
    {
        builder.Property(p => p.BankName).HasMaxLength(80).IsRequired();
        builder.Property(p => p.InterestPct).HasPrecision(5, 2);
        builder.Property(p => p.MinPrice).HasPrecision(18, 2);

        builder.HasIndex(p => new { p.IsActive, p.BankName });
    }
}

public sealed class SlugRedirectConfiguration : IEntityTypeConfiguration<SlugRedirect>
{
    public void Configure(EntityTypeBuilder<SlugRedirect> builder)
    {
        builder.Property(r => r.OldSlug).HasMaxLength(220).IsRequired();
        builder.Property(r => r.NewSlug).HasMaxLength(220).IsRequired();

        // Lookups always go old slug → new slug, and the newest wins.
        builder.HasIndex(r => r.OldSlug);
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.Property(a => a.UserId).HasMaxLength(450);
        builder.Property(a => a.Entity).HasMaxLength(80).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(80).IsRequired();
        builder.Property(a => a.Before).HasColumnType("nvarchar(max)");
        builder.Property(a => a.After).HasColumnType("nvarchar(max)");
        builder.Property(a => a.CorrelationId).HasMaxLength(64);

        builder.HasIndex(a => new { a.Entity, a.EntityId });
        builder.HasIndex(a => a.At);
    }
}

public sealed class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> builder)
    {
        builder.Property(a => a.UserId).HasMaxLength(450).IsRequired();
        builder.Property(a => a.Name).HasMaxLength(120).IsRequired();
        builder.Property(a => a.Phone).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Division).HasMaxLength(60).IsRequired();
        builder.Property(a => a.District).HasMaxLength(60).IsRequired();
        builder.Property(a => a.Thana).HasMaxLength(60).IsRequired();
        builder.Property(a => a.Line).HasMaxLength(300).IsRequired();

        builder.HasIndex(a => a.UserId);
    }
}

public sealed class StockAdjustmentConfiguration : IEntityTypeConfiguration<StockAdjustment>
{
    public void Configure(EntityTypeBuilder<StockAdjustment> builder)
    {
        builder.Property(a => a.Reason).HasMaxLength(200).IsRequired();
        builder.Property(a => a.ChangedBy).HasMaxLength(450);

        // No foreign key on purpose: the log must outlive a deleted variant.
        builder.HasIndex(a => new { a.VariantId, a.At });
        builder.HasIndex(a => a.At);
    }
}
