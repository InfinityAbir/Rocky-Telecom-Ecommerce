using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RockyTelecom.Api.Common;
using RockyTelecom.Application.Features.Admin.Orders;
using RockyTelecom.Application.Features.Admin.Ops;
using RockyTelecom.Application.Features.Orders;
using RockyTelecom.Domain.Constants;
using RockyTelecom.Domain.Enums;

namespace RockyTelecom.Api.Controllers;

/// <summary>Order operations for staff: board, status changes, notes, refunds, invoice (Phase 5.4).</summary>
[Route("api/v{version:apiVersion}/admin/orders")]
[Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
public sealed class AdminOrdersController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public AdminOrdersController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? q,
        [FromQuery] OrderStatus? status,
        [FromQuery] PaymentStatus? paymentStatus,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Envelope(await _mediator.Send(new ListAdminOrdersQuery
        {
            Q = q,
            Status = status,
            PaymentStatus = paymentStatus,
            From = from,
            To = to,
            Page = page,
            PageSize = pageSize
        }, cancellationToken));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new GetOrderQuery { OrderId = id }, cancellationToken));

    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> ChangeStatus(
        int id, [FromBody] ChangeOrderStatusRequest request, CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new ChangeOrderStatusCommand
        {
            OrderId = id,
            Status = request.Status,
            Note = request.Note
        }, cancellationToken), "Order status updated.");

    [HttpPost("{id:int}/notes")]
    public async Task<IActionResult> AddNote(
        int id, [FromBody] AddOrderNoteRequest request, CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new AddOrderNoteCommand
        {
            OrderId = id,
            Note = request.Note
        }, cancellationToken), "Note added.");

    /// <summary>Admins only: a refund is a money movement, not an order-desk action.</summary>
    [HttpPost("{id:int}/refund")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Refund(
        int id, [FromBody] RefundOrderRequest? request, CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new RefundOrderCommand
        {
            OrderId = id,
            Reason = request?.Reason
        }, cancellationToken), "Order marked refunded.");

    [HttpGet("{id:int}/invoice")]
    public async Task<IActionResult> Invoice(int id, CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new GetInvoiceQuery { OrderId = id }, cancellationToken));
}

public sealed record ChangeOrderStatusRequest(OrderStatus Status, string? Note = null);

public sealed record AddOrderNoteRequest(string Note);

public sealed record RefundOrderRequest(string? Reason = null);

/// <summary>Sales/stock overview for the admin home (Phase 5.1).</summary>
[Route("api/v{version:apiVersion}/admin/dashboard")]
[Authorize(Roles = Roles.AdminArea)]
public sealed class AdminDashboardController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public AdminDashboardController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new GetDashboardQuery(), cancellationToken));
}

/// <summary>Store info and delivery charges (Phase 5.7). Only Admin may change them.</summary>
[Route("api/v{version:apiVersion}/admin/settings")]
[Authorize(Roles = Roles.AdminArea)]
public sealed class AdminSettingsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public AdminSettingsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("store")]
    public async Task<IActionResult> GetStore(CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new GetStoreInfoQuery(), cancellationToken));

    [HttpPut("store")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> UpdateStore(
        [FromBody] UpdateStoreInfoCommand command, CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(command, cancellationToken), "Store settings saved.");

    [HttpGet("delivery-zones")]
    public async Task<IActionResult> Zones(
        [FromQuery] bool activeOnly = false, CancellationToken cancellationToken = default)
        => Envelope(await _mediator.Send(new ListDeliveryZonesQuery { ActiveOnly = activeOnly }, cancellationToken));

    [HttpPut("delivery-zones")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> UpsertZone(
        [FromBody] UpsertDeliveryZoneCommand command, CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(command, cancellationToken), "Delivery zone saved.");

    [HttpDelete("delivery-zones/{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> DeleteZone(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteDeliveryZoneCommand { Id = id }, cancellationToken);
        return Envelope(message: "Delivery zone deleted.");
    }
}

/// <summary>Audit trail reader (Phase 5.7) — Admin only, since it exposes staff activity.</summary>
[Route("api/v{version:apiVersion}/admin/audit-log")]
[Authorize(Roles = Roles.Admin)]
public sealed class AdminAuditLogController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public AdminAuditLogController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? entity,
        [FromQuery] string? userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
        => Envelope(await _mediator.Send(new ListAuditLogsQuery
        {
            Entity = entity,
            UserId = userId,
            Page = page,
            PageSize = pageSize
        }, cancellationToken));
}

/// <summary>Announcement ticker lines shown above the header (home content).</summary>
[Route("api/v{version:apiVersion}/admin/news")]
[Authorize(Roles = Roles.Admin + "," + Roles.Staff + "," + Roles.Editor)]
public sealed class AdminNewsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public AdminNewsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new RockyTelecom.Application.Features.Admin.Content.ListNewsItemsQuery(), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] RockyTelecom.Application.Features.Admin.Content.SaveNewsItemCommand command, CancellationToken cancellationToken)
    {
        command.Id = 0;
        var id = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ApiEnvelope.Success(new { id }, "News item created."));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id, [FromBody] RockyTelecom.Application.Features.Admin.Content.SaveNewsItemCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        await _mediator.Send(command, cancellationToken);
        return Envelope(new { id }, "News item updated.");
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new RockyTelecom.Application.Features.Admin.Content.DeleteNewsItemCommand { Id = id }, cancellationToken);
        return Envelope(new { id }, "News item deleted.");
    }
}

/// <summary>Hand-picked home shelves ("Trending now", …) and their ordered products.</summary>
[Route("api/v{version:apiVersion}/admin/showcases")]
[Authorize(Roles = Roles.Admin + "," + Roles.Staff + "," + Roles.Editor)]
public sealed class AdminShowcasesController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public AdminShowcasesController(IMediator mediator) => _mediator = mediator;

    /// <summary>Names-only product search for the shelf editor (no prices or stock).</summary>
    [HttpGet("product-search")]
    public async Task<IActionResult> SearchProducts([FromQuery] string? q, CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(
            new RockyTelecom.Application.Features.Admin.Content.SearchShowcaseProductsQuery { Q = q }, cancellationToken));

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new RockyTelecom.Application.Features.Admin.Content.ListShowcasesQuery(), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] RockyTelecom.Application.Features.Admin.Content.SaveShowcaseCommand command, CancellationToken cancellationToken)
    {
        command.Id = 0;
        var id = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ApiEnvelope.Success(new { id }, "Showcase created."));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id, [FromBody] RockyTelecom.Application.Features.Admin.Content.SaveShowcaseCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        await _mediator.Send(command, cancellationToken);
        return Envelope(new { id }, "Showcase updated.");
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new RockyTelecom.Application.Features.Admin.Content.DeleteShowcaseCommand { Id = id }, cancellationToken);
        return Envelope(new { id }, "Showcase deleted.");
    }
}

/// <summary>Offer pages: banner, text, window and ordered products.</summary>
[Route("api/v{version:apiVersion}/admin/campaigns")]
[Authorize(Roles = Roles.Admin + "," + Roles.Staff + "," + Roles.Editor)]
public sealed class AdminCampaignsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public AdminCampaignsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new RockyTelecom.Application.Features.Admin.Content.ListCampaignsQuery(), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] RockyTelecom.Application.Features.Admin.Content.SaveCampaignCommand command, CancellationToken cancellationToken)
    {
        command.Id = 0;
        var id = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ApiEnvelope.Success(new { id }, "Offer created."));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id, [FromBody] RockyTelecom.Application.Features.Admin.Content.SaveCampaignCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        await _mediator.Send(command, cancellationToken);
        return Envelope(new { id }, "Offer updated.");
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new RockyTelecom.Application.Features.Admin.Content.DeleteCampaignCommand { Id = id }, cancellationToken);
        return Envelope(new { id }, "Offer deleted.");
    }
}

/// <summary>Pre-order request queue for the showroom team.</summary>
[Route("api/v{version:apiVersion}/admin/pre-orders")]
[Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
public sealed class AdminPreOrdersController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public AdminPreOrdersController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] PreOrderStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Envelope(await _mediator.Send(new RockyTelecom.Application.Features.PreOrders.ListAdminPreOrdersQuery
        {
            Status = status,
            Page = page,
            PageSize = pageSize
        }, cancellationToken));

    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> ChangeStatus(
        int id,
        [FromBody] RockyTelecom.Application.Features.PreOrders.ChangePreOrderStatusCommand command,
        CancellationToken cancellationToken)
    {
        command.Id = id;
        await _mediator.Send(command, cancellationToken);
        return Envelope(new { id }, "Pre-order updated.");
    }
}

// ---------------------------------------------------------------------------------------------
// Marketing admin: banners, coupons, EMI plans, flash sales
// ---------------------------------------------------------------------------------------------

[Route("api/v{version:apiVersion}/admin/banners")]
[Authorize(Roles = Roles.Admin + "," + Roles.Staff + "," + Roles.Editor)]
public sealed class AdminBannersController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public AdminBannersController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Envelope(await _mediator.Send(new RockyTelecom.Application.Features.Admin.Content.ListBannersQuery(), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RockyTelecom.Application.Features.Admin.Content.SaveBannerCommand command, CancellationToken ct)
    {
        command.Id = 0;
        var id = await _mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, ApiEnvelope.Success(new { id }, "Banner created."));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] RockyTelecom.Application.Features.Admin.Content.SaveBannerCommand command, CancellationToken ct)
    {
        command.Id = id;
        await _mediator.Send(command, ct);
        return Envelope(new { id }, "Banner updated.");
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _mediator.Send(new RockyTelecom.Application.Features.Admin.Content.DeleteBannerCommand { Id = id }, ct);
        return Envelope(new { id }, "Banner deleted.");
    }
}

/// <summary>Pricing-sensitive: coupons, EMI and flash sales are Admin only (Staff cannot change pricing).</summary>
[Route("api/v{version:apiVersion}/admin/coupons")]
[Authorize(Roles = Roles.Admin)]
public sealed class AdminCouponsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public AdminCouponsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Envelope(await _mediator.Send(new RockyTelecom.Application.Features.Admin.Content.ListCouponsQuery(), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RockyTelecom.Application.Features.Admin.Content.SaveCouponCommand command, CancellationToken ct)
    {
        command.Id = 0;
        var id = await _mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, ApiEnvelope.Success(new { id }, "Coupon created."));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] RockyTelecom.Application.Features.Admin.Content.SaveCouponCommand command, CancellationToken ct)
    {
        command.Id = id;
        await _mediator.Send(command, ct);
        return Envelope(new { id }, "Coupon updated.");
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _mediator.Send(new RockyTelecom.Application.Features.Admin.Content.DeleteCouponCommand { Id = id }, ct);
        return Envelope(new { id }, "Coupon deleted.");
    }
}

[Route("api/v{version:apiVersion}/admin/emi-plans")]
[Authorize(Roles = Roles.Admin)]
public sealed class AdminEmiPlansController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public AdminEmiPlansController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Envelope(await _mediator.Send(new RockyTelecom.Application.Features.Admin.Content.ListEmiPlansQuery(), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RockyTelecom.Application.Features.Admin.Content.SaveEmiPlanCommand command, CancellationToken ct)
    {
        command.Id = 0;
        var id = await _mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, ApiEnvelope.Success(new { id }, "EMI plan created."));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] RockyTelecom.Application.Features.Admin.Content.SaveEmiPlanCommand command, CancellationToken ct)
    {
        command.Id = id;
        await _mediator.Send(command, ct);
        return Envelope(new { id }, "EMI plan updated.");
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _mediator.Send(new RockyTelecom.Application.Features.Admin.Content.DeleteEmiPlanCommand { Id = id }, ct);
        return Envelope(new { id }, "EMI plan deleted.");
    }
}

[Route("api/v{version:apiVersion}/admin/flash-sales")]
[Authorize(Roles = Roles.Admin)]
public sealed class AdminFlashSalesController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public AdminFlashSalesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Envelope(await _mediator.Send(new RockyTelecom.Application.Features.Admin.Content.ListFlashSalesQuery(), ct));

    [HttpGet("variant-search")]
    public async Task<IActionResult> SearchVariants([FromQuery] string? q, CancellationToken ct)
        => Envelope(await _mediator.Send(new RockyTelecom.Application.Features.Admin.Content.SearchVariantsQuery { Q = q }, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RockyTelecom.Application.Features.Admin.Content.SaveFlashSaleCommand command, CancellationToken ct)
    {
        command.Id = 0;
        var id = await _mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, ApiEnvelope.Success(new { id }, "Flash sale created."));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] RockyTelecom.Application.Features.Admin.Content.SaveFlashSaleCommand command, CancellationToken ct)
    {
        command.Id = id;
        await _mediator.Send(command, ct);
        return Envelope(new { id }, "Flash sale updated.");
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _mediator.Send(new RockyTelecom.Application.Features.Admin.Content.DeleteFlashSaleCommand { Id = id }, ct);
        return Envelope(new { id }, "Flash sale deleted.");
    }
}

[Route("api/v{version:apiVersion}/admin/blogs")]
[Authorize(Roles = Roles.Admin + "," + Roles.Staff + "," + Roles.Editor)]
public sealed class AdminBlogsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public AdminBlogsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Envelope(await _mediator.Send(new RockyTelecom.Application.Features.Blogs.ListAdminBlogsQuery(), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RockyTelecom.Application.Features.Blogs.SaveBlogCommand command, CancellationToken ct)
    {
        command.Id = 0;
        var id = await _mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, ApiEnvelope.Success(new { id }, "Post saved."));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] RockyTelecom.Application.Features.Blogs.SaveBlogCommand command, CancellationToken ct)
    {
        command.Id = id;
        await _mediator.Send(command, ct);
        return Envelope(new { id }, "Post updated.");
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _mediator.Send(new RockyTelecom.Application.Features.Blogs.DeleteBlogCommand { Id = id }, ct);
        return Envelope(new { id }, "Post deleted.");
    }
}

[Route("api/v{version:apiVersion}/admin/reviews")]
[Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
public sealed class AdminReviewsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public AdminReviewsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string status = "pending", CancellationToken ct = default)
        => Envelope(await _mediator.Send(new RockyTelecom.Application.Features.Reviews.ListAdminReviewsQuery { Status = status }, ct));

    [HttpPost("{id:int}/moderate")]
    public async Task<IActionResult> Moderate(int id, [FromBody] RockyTelecom.Application.Features.Reviews.ModerateReviewCommand command, CancellationToken ct)
    {
        command.Id = id;
        await _mediator.Send(command, ct);
        return Envelope(new { id }, command.Approve ? "Review published." : "Review removed.");
    }
}

[Route("api/v{version:apiVersion}/admin/inventory")]
[Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
public sealed class AdminInventoryController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public AdminInventoryController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? q, [FromQuery] bool lowOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Envelope(await _mediator.Send(new RockyTelecom.Application.Features.Admin.Inventory.ListInventoryQuery { Q = q, LowOnly = lowOnly, Page = page, PageSize = pageSize }, ct));

    [HttpPost("{variantId:int}/adjust")]
    public async Task<IActionResult> Adjust(int variantId, [FromBody] RockyTelecom.Application.Features.Admin.Inventory.AdjustStockCommand command, CancellationToken ct)
    {
        command.VariantId = variantId;
        var stock = await _mediator.Send(command, ct);
        return Envelope(new { variantId, stock }, "Stock updated.");
    }

    [HttpGet("log")]
    public async Task<IActionResult> Log([FromQuery] int? variantId, [FromQuery] int take = 30, CancellationToken ct = default)
        => Envelope(await _mediator.Send(new RockyTelecom.Application.Features.Admin.Inventory.ListStockLogQuery { VariantId = variantId, Take = take }, ct));
}
