using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RockyTelecom.Api.Common;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Features.Admin.Catalog;
using RockyTelecom.Application.Features.Admin.Users;
using RockyTelecom.Domain.Constants;

namespace RockyTelecom.Api.Controllers;

/// <summary>
/// Admin catalog (Phase 5.2). Pricing and product writing are staff work; deleting and publishing
/// stay with Admin/Staff per the role matrix in ARCHITECTURE §7.
/// </summary>
[Route("api/v{version:apiVersion}/admin/products")]
[Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
public sealed class AdminProductsController : ApiControllerBase
{
    private const int MaxUploadBytes = 5 * 1024 * 1024;
    private const int MaxUploadsPerRequest = 5;

    private static readonly string[] AllowedContentTypes =
    {
        "image/jpeg", "image/png", "image/webp"
    };

    private readonly IMediator _mediator;
    private readonly IImageStore _images;

    public AdminProductsController(IMediator mediator, IImageStore images)
    {
        _mediator = mediator;
        _images = images;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int? categoryId,
        [FromQuery] int? brandId,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Envelope(await _mediator.Send(new ListAdminProductsQuery
        {
            Search = search,
            Status = status,
            CategoryId = categoryId,
            BrandId = brandId,
            Sort = sort,
            Page = page,
            PageSize = pageSize
        }, cancellationToken));

    /// <summary>CSV export (or a blank template with one example row). Admin only.</summary>
    [HttpGet("export")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Export(
        [FromQuery] string? categorySlug, [FromQuery] bool template = false, CancellationToken cancellationToken = default)
    {
        var file = await _mediator.Send(
            new ExportProductsQuery { CategorySlug = categorySlug, TemplateOnly = template }, cancellationToken);

        // UTF-8 with BOM so Excel opens Bangla/accents correctly.
        var bytes = new System.Text.UTF8Encoding(true).GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(file.Content)).ToArray();
        return File(bytes, "text/csv; charset=utf-8", file.FileName);
    }

    /// <summary>
    /// CSV import. <c>dryRun=true</c> validates and reports without writing. Any error means nothing is
    /// written (all-or-nothing). Admin only.
    /// </summary>
    [HttpPost("import")]
    [Authorize(Roles = Roles.Admin)]
    [RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<IActionResult> Import(
        IFormFile? file, [FromQuery] bool dryRun = true, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(ApiEnvelope.Failure("Choose a CSV file."));
        }

        if (file.Length > 2 * 1024 * 1024)
        {
            return BadRequest(ApiEnvelope.Failure("The file is larger than 2 MB."));
        }

        using var reader = new StreamReader(file.OpenReadStream(), System.Text.Encoding.UTF8, true);
        var csv = await reader.ReadToEndAsync(cancellationToken);
        var report = await _mediator.Send(new ImportProductsCommand { Csv = csv, DryRun = dryRun }, cancellationToken);

        var message = report.Errors.Count > 0
            ? $"{report.Errors.Count} problem(s) found. Nothing was imported."
            : report.Applied ? "Import complete." : "No problems found. Ready to import.";
        return Envelope(report, message);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new GetAdminProductQuery { Id = id }, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateProductCommand command, CancellationToken cancellationToken)
    {
        var id = await _mediator.Send(command, cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            ApiEnvelope.Success(new { id }, "Draft product created."));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id, [FromBody] UpdateProductCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        await _mediator.Send(command, cancellationToken);

        return Envelope(new { id }, "Product updated.");
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteProductCommand { Id = id }, cancellationToken);
        return Envelope(message: "Product deleted.");
    }

    [HttpPost("{id:int}/publish")]
    public async Task<IActionResult> Publish(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new PublishProductCommand { Id = id }, cancellationToken);
        return Envelope(message: "Product published.");
    }

    [HttpPost("{id:int}/unpublish")]
    public async Task<IActionResult> Unpublish(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new UnpublishProductCommand { Id = id }, cancellationToken);
        return Envelope(message: "Product unpublished.");
    }

    [HttpPost("{id:int}/duplicate")]
    public async Task<IActionResult> Duplicate(int id, CancellationToken cancellationToken)
    {
        var copyId = await _mediator.Send(new DuplicateProductCommand { Id = id }, cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            ApiEnvelope.Success(new { id = copyId }, "Product duplicated as a draft."));
    }

    [HttpGet("{id:int}/specs")]
    public async Task<IActionResult> Specs(int id, CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new GetProductSpecSheetQuery { ProductId = id }, cancellationToken));

    [HttpPut("{id:int}/specs")]
    public async Task<IActionResult> SaveSpecs(
        int id, [FromBody] SaveProductSpecsRequest request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new SaveProductSpecsCommand
        {
            ProductId = id,
            Values = request.Values ?? Array.Empty<ProductSpecValueInput>()
        }, cancellationToken);

        return Envelope(message: "Specs saved.");
    }

    /// <summary>
    /// Uploads product images. The store re-encodes every file to WebP at three widths and never
    /// serves the original, so a disguised file cannot become executable content (D-14).
    /// </summary>
    [HttpPost("images")]
    [RequestSizeLimit(MaxUploadsPerRequest * MaxUploadBytes)]
    public async Task<IActionResult> Upload(
        [FromForm] List<IFormFile>? files, CancellationToken cancellationToken)
    {
        files ??= new List<IFormFile>();

        if (files.Count == 0)
        {
            return BadRequest(ApiEnvelope.Failure("Choose at least one image."));
        }

        if (files.Count > MaxUploadsPerRequest)
        {
            return BadRequest(ApiEnvelope.Failure($"Upload at most {MaxUploadsPerRequest} images at a time."));
        }

        var stored = new List<object>(files.Count);

        foreach (var file in files)
        {
            if (!AllowedContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
            {
                return BadRequest(ApiEnvelope.Failure("Only JPEG, PNG and WebP images are accepted."));
            }

            if (file.Length <= 0 || file.Length > MaxUploadBytes)
            {
                return BadRequest(ApiEnvelope.Failure("Each image must be between 1 byte and 5 MB."));
            }

            await using var stream = file.OpenReadStream();
            var image = await _images.SaveAsync(stream, file.FileName, cancellationToken);

            stored.Add(new
            {
                url = _images.GetPublicUrl(image.Large),
                thumbUrl = _images.GetPublicUrl(image.Thumb),
                mediumUrl = _images.GetPublicUrl(image.Medium)
            });
        }

        return Envelope(stored, "Images uploaded.");
    }
}

public sealed record SaveProductSpecsRequest(IReadOnlyList<ProductSpecValueInput>? Values);

[Route("api/v{version:apiVersion}/admin/categories")]
[Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
public sealed class AdminCategoriesController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public AdminCategoriesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new ListAdminCategoriesQuery(), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        var id = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ApiEnvelope.Success(new { id }, "Category created."));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id, [FromBody] UpdateCategoryCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        await _mediator.Send(command, cancellationToken);

        return Envelope(new { id }, "Category updated.");
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteCategoryCommand { Id = id }, cancellationToken);
        return Envelope(message: "Category deleted.");
    }
}

[Route("api/v{version:apiVersion}/admin/brands")]
[Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
public sealed class AdminBrandsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public AdminBrandsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new ListAdminBrandsQuery(), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateBrandCommand command, CancellationToken cancellationToken)
    {
        var id = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ApiEnvelope.Success(new { id }, "Brand created."));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id, [FromBody] UpdateBrandCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        await _mediator.Send(command, cancellationToken);

        return Envelope(new { id }, "Brand updated.");
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteBrandCommand { Id = id }, cancellationToken);
        return Envelope(message: "Brand deleted.");
    }
}

/// <summary>Spec templates are the only way fields enter the catalogue (FR-16).</summary>
[Route("api/v{version:apiVersion}/admin/spec-templates")]
[Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
public sealed class AdminSpecTemplatesController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public AdminSpecTemplatesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] int categoryId, CancellationToken cancellationToken = default)
        => Envelope(await _mediator.Send(new GetSpecTemplateQuery { CategoryId = categoryId }, cancellationToken));

    [HttpPut]
    public async Task<IActionResult> Save(
        [FromBody] SaveSpecTemplateCommand command, CancellationToken cancellationToken)
    {
        var id = await _mediator.Send(command, cancellationToken);
        return Envelope(new { id }, "Spec template saved.");
    }
}

/// <summary>Users and roles are Admin-only: Staff must never grant privileges (D-10).</summary>
[Route("api/v{version:apiVersion}/admin/users")]
[Authorize(Roles = Roles.Admin)]
public sealed class AdminUsersController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public AdminUsersController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Envelope(await _mediator.Send(new ListUsersQuery
        {
            Search = search,
            Page = page,
            PageSize = pageSize
        }, cancellationToken));

    [HttpPost("{id}/block")]
    public async Task<IActionResult> Block(
        string id, [FromBody] BlockUserRequest request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new SetUserBlockedCommand { UserId = id, Blocked = request.Blocked }, cancellationToken);
        return Envelope(message: request.Blocked ? "Account blocked." : "Account unblocked.");
    }

    [HttpPost("{id}/roles")]
    public async Task<IActionResult> AssignRole(
        string id, [FromBody] AssignRoleRequest request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new AssignRoleCommand { UserId = id, Role = request.Role }, cancellationToken);
        return Envelope(message: $"Role {request.Role} assigned.");
    }
}

public sealed record BlockUserRequest(bool Blocked);

public sealed record AssignRoleRequest(string Role);
