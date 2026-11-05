using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RockyTelecom.Domain.Entities;

namespace RockyTelecom.Infrastructure.Persistence.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(120).IsRequired();
        builder.Property(c => c.Slug).HasMaxLength(140).IsRequired();
        builder.Property(c => c.Icon).HasMaxLength(64);
        builder.Property(c => c.SeoTitle).HasMaxLength(70);
        builder.Property(c => c.SeoDesc).HasMaxLength(160);

        builder.HasIndex(c => c.Slug).IsUnique();
        builder.HasIndex(c => c.ParentId);

        // Self-reference must not cascade or SQL Server rejects the schema.
        builder.HasOne(c => c.Parent)
            .WithMany(c => c.Children)
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        builder.Property(b => b.Name).HasMaxLength(80).IsRequired();
        builder.Property(b => b.Slug).HasMaxLength(100).IsRequired();
        builder.Property(b => b.LogoUrl).HasMaxLength(512);

        builder.HasIndex(b => b.Slug).IsUnique();
    }
}

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Slug).HasMaxLength(220).IsRequired();
        builder.Property(p => p.ShortDesc).HasMaxLength(300);
        builder.Property(p => p.Badge).HasMaxLength(30);
        builder.Property(p => p.SeoTitle).HasMaxLength(70);
        builder.Property(p => p.SeoDesc).HasMaxLength(160);

        builder.HasIndex(p => p.Slug).IsUnique();
        builder.HasIndex(p => new { p.IsActive, p.CategoryId });
        builder.HasIndex(p => new { p.IsActive, p.IsFeatured });
        builder.HasIndex(p => p.CategoryId);

        builder.HasOne(p => p.Brand)
            .WithMany(b => b.Products)
            .HasForeignKey(p => p.BrandId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.Property(v => v.Sku).HasMaxLength(60).IsRequired();
        builder.Property(v => v.Color).HasMaxLength(40);
        builder.Property(v => v.ColorHex).HasMaxLength(9);
        builder.Property(v => v.Ram).HasMaxLength(20);
        builder.Property(v => v.Storage).HasMaxLength(20);
        builder.Property(v => v.Region).HasMaxLength(40);
        builder.Property(v => v.SimType).HasMaxLength(20);

        builder.Property(v => v.RegularPrice).HasPrecision(18, 2);
        builder.Property(v => v.SalePrice).HasPrecision(18, 2);

        // Stock decrements are guarded by this token so two buyers cannot oversell one unit (FR-03).
        builder.Property(v => v.RowVersion).IsRowVersion();

        builder.HasIndex(v => v.Sku).IsUnique();
        builder.HasIndex(v => v.ProductId);

        builder.HasOne(v => v.Product)
            .WithMany(p => p.Variants)
            .HasForeignKey(v => v.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.Property(i => i.Url).HasMaxLength(512).IsRequired();
        builder.Property(i => i.Alt).HasMaxLength(200);

        builder.HasIndex(i => i.ProductId);
        builder.HasIndex(i => i.VariantId);

        builder.HasOne(i => i.Product)
            .WithMany(p => p.Images)
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // NoAction: Product→Image already cascades, so a second cascade path is not allowed.
        builder.HasOne(i => i.Variant)
            .WithMany(v => v.Images)
            .HasForeignKey(i => i.VariantId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class SpecTemplateConfiguration : IEntityTypeConfiguration<SpecTemplate>
{
    public void Configure(EntityTypeBuilder<SpecTemplate> builder)
    {
        builder.Property(t => t.Name).HasMaxLength(120).IsRequired();
        builder.HasIndex(t => t.CategoryId).IsUnique();

        builder.HasOne(t => t.Category)
            .WithMany()
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SpecGroupConfiguration : IEntityTypeConfiguration<SpecGroup>
{
    public void Configure(EntityTypeBuilder<SpecGroup> builder)
    {
        builder.Property(g => g.Name).HasMaxLength(80).IsRequired();

        builder.HasOne(g => g.Template)
            .WithMany(t => t.Groups)
            .HasForeignKey(g => g.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SpecFieldConfiguration : IEntityTypeConfiguration<SpecField>
{
    public void Configure(EntityTypeBuilder<SpecField> builder)
    {
        builder.Property(f => f.Key).HasMaxLength(60).IsRequired();
        builder.Property(f => f.Label).HasMaxLength(120).IsRequired();
        builder.Property(f => f.Unit).HasMaxLength(20);
        builder.Property(f => f.Options).HasMaxLength(512);

        builder.HasIndex(f => new { f.GroupId, f.Key }).IsUnique();

        builder.HasOne(f => f.Group)
            .WithMany(g => g.Fields)
            .HasForeignKey(f => f.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ProductSpecConfiguration : IEntityTypeConfiguration<ProductSpec>
{
    public void Configure(EntityTypeBuilder<ProductSpec> builder)
    {
        builder.Property(s => s.Value).HasMaxLength(500);
        builder.Property(s => s.NumericValue).HasPrecision(18, 4);

        builder.HasIndex(s => new { s.ProductId, s.SpecFieldId }).IsUnique();

        builder.HasOne(s => s.Product)
            .WithMany(p => p.Specs)
            .HasForeignKey(s => s.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // NoAction: the template path already cascades down to ProductSpec-adjacent rows.
        builder.HasOne(s => s.SpecField)
            .WithMany()
            .HasForeignKey(s => s.SpecFieldId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class VariantOptionConfiguration : IEntityTypeConfiguration<VariantOption>
{
    public void Configure(EntityTypeBuilder<VariantOption> builder)
    {
        builder.HasIndex(v => new { v.CategoryId, v.Axis }).IsUnique();

        builder.HasOne(v => v.Category)
            .WithMany(c => c.VariantOptions)
            .HasForeignKey(v => v.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
