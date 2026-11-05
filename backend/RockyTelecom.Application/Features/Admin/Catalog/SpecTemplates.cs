using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Constants;
using RockyTelecom.Domain.Entities;
using RockyTelecom.Domain.Enums;

namespace RockyTelecom.Application.Features.Admin.Catalog;

public sealed record SpecFieldDto(
    int Id,
    string Key,
    string Label,
    SpecDataType DataType,
    string? Unit,
    string? Options,
    bool IsRequired,
    bool IsFilterable,
    bool IsComparable,
    int SortOrder);

public sealed record SpecTemplateGroupDto(int Id, string Name, int SortOrder, IReadOnlyList<SpecFieldDto> Fields);

public sealed record SpecTemplateDto(
    int Id,
    int CategoryId,
    string CategoryName,
    string Name,
    IReadOnlyList<SpecTemplateGroupDto> Groups);

/// <summary>Reads the spec template attached to a category, if any (FR-16).</summary>
public sealed class GetSpecTemplateQuery : IQuery<SpecTemplateDto?>
{
    public int CategoryId { get; init; }
}

public sealed class GetSpecTemplateQueryHandler : IRequestHandler<GetSpecTemplateQuery, SpecTemplateDto?>
{
    private readonly IApplicationDbContext _db;

    public GetSpecTemplateQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<SpecTemplateDto?> Handle(GetSpecTemplateQuery request, CancellationToken cancellationToken)
    {
        var template = await _db.SpecTemplates
            .AsNoTracking()
            .Include(t => t.Category)
            .Include(t => t.Groups)
                .ThenInclude(g => g.Fields)
            .FirstOrDefaultAsync(t => t.CategoryId == request.CategoryId, cancellationToken);

        if (template is null)
        {
            return null;
        }

        var groups = template.Groups
            .OrderBy(g => g.SortOrder)
            .Select(g => new SpecTemplateGroupDto(
                g.Id,
                g.Name,
                g.SortOrder,
                g.Fields
                    .OrderBy(f => f.SortOrder)
                    .Select(f => new SpecFieldDto(
                        f.Id, f.Key, f.Label, f.DataType, f.Unit, f.Options, f.IsRequired, f.IsFilterable, f.IsComparable, f.SortOrder))
                    .ToList()))
            .ToList();

        return new SpecTemplateDto(
            template.Id,
            template.CategoryId,
            template.Category?.Name ?? string.Empty,
            template.Name,
            groups);
    }
}

public sealed record SpecFieldInput(
    int? Id,
    string Key,
    string Label,
    SpecDataType DataType,
    string? Unit,
    string? Options,
    bool IsRequired,
    bool IsFilterable,
    bool IsComparable,
    int SortOrder);

public sealed record SpecGroupInput(int? Id, string Name, int SortOrder, IReadOnlyList<SpecFieldInput> Fields);

/// <summary>
/// Creates or replaces the spec template for a category (FR-16). Fields that already carry product
/// values cannot be removed — they are deactivated by clearing <c>IsRequired</c> instead.
/// </summary>
public sealed class SaveSpecTemplateCommand : ICommand<int>
{
    public int CategoryId { get; init; }
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<SpecGroupInput> Groups { get; init; } = Array.Empty<SpecGroupInput>();
}

public sealed class SaveSpecTemplateCommandValidator : AbstractValidator<SaveSpecTemplateCommand>
{
    public SaveSpecTemplateCommandValidator()
    {
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);

        RuleForEach(x => x.Groups).ChildRules(group =>
        {
            group.RuleFor(g => g.Name).NotEmpty().MaximumLength(80);

            group.RuleForEach(g => g.Fields).ChildRules(field =>
            {
                field.RuleFor(f => f.Key).NotEmpty().MaximumLength(60);
                field.RuleFor(f => f.Label).NotEmpty().MaximumLength(120);
                // FR-19: a filterable numeric field stores a parsed NumericValue, so it must be numeric.
                field.RuleFor(f => f.Options)
                    .NotEmpty()
                    .When(f => f.DataType == SpecDataType.Option)
                    .WithMessage("Option fields must list their allowed values.");
                field.RuleFor(f => f.IsFilterable)
                    .Equal(false)
                    .When(f => f.DataType is SpecDataType.Text or SpecDataType.Bool)
                    .WithMessage("Only number and option fields can be filterable.");
            });
        });
    }
}

public sealed class SaveSpecTemplateCommandHandler : IRequestHandler<SaveSpecTemplateCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;
    private readonly IDateTimeProvider _clock;

    public SaveSpecTemplateCommandHandler(IApplicationDbContext db, ICache cache, IDateTimeProvider clock)
    {
        _db = db;
        _cache = cache;
        _clock = clock;
    }

    public async Task<int> Handle(SaveSpecTemplateCommand request, CancellationToken cancellationToken)
    {
        if (!await _db.Categories.AnyAsync(c => c.Id == request.CategoryId, cancellationToken))
        {
            throw new NotFoundException("Category", request.CategoryId);
        }

        var template = await _db.SpecTemplates
            .Include(t => t.Groups)
                .ThenInclude(g => g.Fields)
            .FirstOrDefaultAsync(t => t.CategoryId == request.CategoryId, cancellationToken);

        if (template is null)
        {
            template = new SpecTemplate
            {
                CategoryId = request.CategoryId,
                Name = request.Name.Trim(),
                CreatedAt = _clock.UtcNow
            };
            _db.SpecTemplates.Add(template);
        }
        else
        {
            template.Name = request.Name.Trim();
            template.UpdatedAt = _clock.UtcNow;
        }

        var existingGroups = template.Groups.ToList();
        var existingFields = existingGroups.SelectMany(g => g.Fields).ToList();

        var keptGroupIds = request.Groups.Where(g => g.Id.HasValue).Select(g => g.Id!.Value).ToHashSet();
        var keptFieldIds = request.Groups.SelectMany(g => g.Fields).Where(f => f.Id.HasValue).Select(f => f.Id!.Value).ToHashSet();

        var removedGroupIds = existingGroups.Where(g => !keptGroupIds.Contains(g.Id)).Select(g => g.Id).ToList();
        var removedFieldIds = existingFields.Where(f => !keptFieldIds.Contains(f.Id)).Select(f => f.Id).ToList();

        if (removedFieldIds.Count > 0)
        {
            var inUse = await _db.ProductSpecs
                .Where(s => removedFieldIds.Contains(s.SpecFieldId) && s.Value != null && s.Value != "")
                .Select(s => s.SpecFieldId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (inUse.Count > 0)
            {
                throw new ConflictException(
                    "Some fields are in use by products and cannot be removed. Clear their values first.");
            }
        }

        foreach (var removedField in existingFields.Where(f => removedFieldIds.Contains(f.Id)))
        {
            _db.SpecFields.Remove(removedField);
        }

        foreach (var removedGroup in existingGroups.Where(g => removedGroupIds.Contains(g.Id)))
        {
            _db.SpecGroups.Remove(removedGroup);
        }

        foreach (var groupInput in request.Groups)
        {
            var group = groupInput.Id.HasValue
                ? existingGroups.FirstOrDefault(g => g.Id == groupInput.Id.Value)
                : null;

            if (group is null)
            {
                group = new SpecGroup { Template = template, Name = groupInput.Name.Trim(), SortOrder = groupInput.SortOrder };
                template.Groups.Add(group);
            }
            else
            {
                group.Name = groupInput.Name.Trim();
                group.SortOrder = groupInput.SortOrder;
            }

            foreach (var fieldInput in groupInput.Fields)
            {
                var field = fieldInput.Id.HasValue
                    ? existingFields.FirstOrDefault(f => f.Id == fieldInput.Id.Value)
                    : null;

                if (field is null)
                {
                    field = new SpecField();
                    group.Fields.Add(field);
                }

                field.Key = fieldInput.Key.Trim();
                field.Label = fieldInput.Label.Trim();
                field.DataType = fieldInput.DataType;
                field.Unit = fieldInput.Unit;
                field.Options = fieldInput.Options;
                field.IsRequired = fieldInput.IsRequired;
                field.IsFilterable = fieldInput.IsFilterable;
                field.IsComparable = fieldInput.IsComparable;
                field.SortOrder = fieldInput.SortOrder;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CacheKeys.SpecTemplates, cancellationToken);

        return template.Id;
    }
}
