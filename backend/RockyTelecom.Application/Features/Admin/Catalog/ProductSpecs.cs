using System.Globalization;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Entities;
using RockyTelecom.Domain.Enums;

namespace RockyTelecom.Application.Features.Admin.Catalog;

public sealed record ProductSpecValueDto(
    int SpecFieldId,
    string Key,
    string Label,
    string? Unit,
    string? Options,
    SpecDataType DataType,
    bool IsRequired,
    bool IsFilterable,
    bool IsComparable,
    int SortOrder,
    string? Value,
    decimal? NumericValue);

public sealed record ProductSpecGroupDto(string Name, int SortOrder, IReadOnlyList<ProductSpecValueDto> Fields);

/// <summary>
/// The spec sheet an admin fills in for one product: template fields joined with saved values.
/// </summary>
public sealed record ProductSpecSheetDto(
    int ProductId,
    string ProductName,
    int? TemplateId,
    string? TemplateName,
    IReadOnlyList<ProductSpecGroupDto> Groups);

public sealed class GetProductSpecSheetQuery : IQuery<ProductSpecSheetDto>
{
    public int ProductId { get; init; }
}

public sealed class GetProductSpecSheetQueryHandler : IRequestHandler<GetProductSpecSheetQuery, ProductSpecSheetDto>
{
    private readonly IApplicationDbContext _db;

    public GetProductSpecSheetQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<ProductSpecSheetDto> Handle(GetProductSpecSheetQuery request, CancellationToken cancellationToken)
    {
        var product = await _db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken)
            ?? throw new NotFoundException("Product", request.ProductId);

        var template = await _db.SpecTemplates
            .AsNoTracking()
            .Include(t => t.Groups)
                .ThenInclude(g => g.Fields)
            .FirstOrDefaultAsync(t => t.CategoryId == product.CategoryId, cancellationToken);

        var values = await _db.ProductSpecs
            .AsNoTracking()
            .Where(s => s.ProductId == product.Id)
            .ToDictionaryAsync(s => s.SpecFieldId, cancellationToken);

        var groups = template?.Groups
            .OrderBy(g => g.SortOrder)
            .Select(g => new ProductSpecGroupDto(
                g.Name,
                g.SortOrder,
                g.Fields
                    .OrderBy(f => f.SortOrder)
                    .Select(f =>
                    {
                        values.TryGetValue(f.Id, out var saved);
                        return new ProductSpecValueDto(
                            f.Id, f.Key, f.Label, f.Unit, f.Options, f.DataType,
                            f.IsRequired, f.IsFilterable, f.IsComparable, f.SortOrder,
                            saved?.Value, saved?.NumericValue);
                    })
                    .ToList()))
            .ToList()
            ?? new List<ProductSpecGroupDto>();

        return new ProductSpecSheetDto(product.Id, product.Name, template?.Id, template?.Name, groups);
    }
}

public sealed record ProductSpecValueInput(int SpecFieldId, string? Value);

/// <summary>
/// Saves the spec values for one product. Missing required fields are allowed here; they block
/// publishing instead (FR-20). Numeric values are parsed server-side for range filtering (FR-19).
/// </summary>
public sealed class SaveProductSpecsCommand : ICommand<Unit>
{
    public int ProductId { get; init; }
    public IReadOnlyList<ProductSpecValueInput> Values { get; init; } = Array.Empty<ProductSpecValueInput>();
}

public sealed class SaveProductSpecsCommandValidator : AbstractValidator<SaveProductSpecsCommand>
{
    public SaveProductSpecsCommandValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
    }
}

public sealed class SaveProductSpecsCommandHandler : IRequestHandler<SaveProductSpecsCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public SaveProductSpecsCommandHandler(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Unit> Handle(SaveProductSpecsCommand request, CancellationToken cancellationToken)
    {
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken)
            ?? throw new NotFoundException("Product", request.ProductId);

        var template = await _db.SpecTemplates
            .AsNoTracking()
            .Include(t => t.Groups)
                .ThenInclude(g => g.Fields)
            .FirstOrDefaultAsync(t => t.CategoryId == product.CategoryId, cancellationToken);

        var fields = template?.Groups.SelectMany(g => g.Fields).ToDictionary(f => f.Id)
            ?? new Dictionary<int, SpecField>();

        var unknown = request.Values
            .Where(v => !fields.ContainsKey(v.SpecFieldId))
            .Select(v => v.SpecFieldId)
            .Distinct()
            .ToList();

        if (unknown.Count > 0)
        {
            throw new DomainValidationException(
                "Spec fields do not belong to this product's category template.",
                new Dictionary<string, string[]> { ["values"] = new[] { $"Unknown field ids: {string.Join(", ", unknown)}." } });
        }

        var existing = await _db.ProductSpecs
            .Where(s => s.ProductId == product.Id)
            .ToListAsync(cancellationToken);

        var numericErrors = new Dictionary<string, List<string>>();

        foreach (var value in request.Values)
        {
            var field = fields[value.SpecFieldId];
            var spec = existing.FirstOrDefault(s => s.SpecFieldId == value.SpecFieldId);

            if (spec is null)
            {
                spec = new ProductSpec { ProductId = product.Id, SpecFieldId = value.SpecFieldId };
                _db.ProductSpecs.Add(spec);
                existing.Add(spec);
            }

            spec.Value = string.IsNullOrWhiteSpace(value.Value) ? null : value.Value.Trim();
            spec.SortOrder = field.SortOrder;
            spec.NumericValue = null;

            if (field.DataType == SpecDataType.Number && spec.Value is not null)
            {
                if (decimal.TryParse(spec.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var numeric))
                {
                    spec.NumericValue = numeric;
                }
                else if (field.IsFilterable)
                {
                    // FR-19: filterable numeric fields must be parseable or the filter breaks.
                    if (!numericErrors.TryGetValue(field.Label, out var messages))
                    {
                        messages = new List<string>();
                        numericErrors[field.Label] = messages;
                    }

                    messages.Add("Must be a number.");
                }
            }
        }

        if (numericErrors.Count > 0)
        {
            throw new DomainValidationException(
                "Some numeric fields could not be parsed.",
                numericErrors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()));
        }

        product.UpdatedAt = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
