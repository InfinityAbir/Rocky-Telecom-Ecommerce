using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RockyTelecom.Api.Common;
using RockyTelecom.Application.Features.Account;
using RockyTelecom.Application.Features.Forms;
using RockyTelecom.Domain.Constants;

namespace RockyTelecom.Api.Controllers;

/// <summary>The signed-in customer's own profile, password and saved addresses.</summary>
[Route("api/v{version:apiVersion}/me")]
[Authorize]
public sealed class MeController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public MeController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> Profile(CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new GetProfileQuery(), cancellationToken));

    [HttpPut]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileCommand command, CancellationToken cancellationToken)
    {
        await _mediator.Send(command, cancellationToken);
        return Envelope(await _mediator.Send(new GetProfileQuery(), cancellationToken), "Profile updated.");
    }

    [HttpPost("password")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        await _mediator.Send(command, cancellationToken);
        return Envelope(new { changed = true }, "Password changed.");
    }

    [HttpGet("addresses")]
    public async Task<IActionResult> Addresses(CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new GetAddressesQuery(), cancellationToken));

    [HttpPost("addresses")]
    public async Task<IActionResult> CreateAddress([FromBody] CreateAddressCommand command, CancellationToken cancellationToken)
    {
        var id = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ApiEnvelope.Success(new { id }, "Address saved."));
    }

    [HttpPut("addresses/{id:int}")]
    public async Task<IActionResult> UpdateAddress(int id, [FromBody] UpdateAddressCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        await _mediator.Send(command, cancellationToken);
        return Envelope(new { id }, "Address updated.");
    }

    [HttpDelete("addresses/{id:int}")]
    public async Task<IActionResult> DeleteAddress(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteAddressCommand { Id = id }, cancellationToken);
        return Envelope(new { id }, "Address removed.");
    }
}

/// <summary>Public contact forms. All are rate-limited per client and return a short receipt.</summary>
public sealed class FormsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public FormsController(IMediator mediator) => _mediator = mediator;

    [HttpPost("api/v{version:apiVersion}/support")]
    [EnableRateLimiting("forms")]
    public async Task<IActionResult> Support([FromBody] SubmitSupportTicketCommand command, CancellationToken cancellationToken)
        => Created(await _mediator.Send(command, cancellationToken), "Support request received.");

    [HttpPost("api/v{version:apiVersion}/trade-in")]
    [EnableRateLimiting("forms")]
    public async Task<IActionResult> TradeIn([FromBody] SubmitTradeInCommand command, CancellationToken cancellationToken)
        => Created(await _mediator.Send(command, cancellationToken), "Trade-in request received.");

    [HttpPost("api/v{version:apiVersion}/corporate")]
    [EnableRateLimiting("forms")]
    public async Task<IActionResult> Corporate([FromBody] SubmitCorporateInquiryCommand command, CancellationToken cancellationToken)
        => Created(await _mediator.Send(command, cancellationToken), "Inquiry received.");

    [HttpPost("api/v{version:apiVersion}/feedback")]
    [EnableRateLimiting("forms")]
    public async Task<IActionResult> Feedback([FromBody] SubmitFeedbackCommand command, CancellationToken cancellationToken)
        => Created(await _mediator.Send(command, cancellationToken), "Thank you for your feedback.");

    [HttpPost("api/v{version:apiVersion}/newsletter")]
    [EnableRateLimiting("forms")]
    public async Task<IActionResult> Newsletter([FromBody] SubscribeNewsletterCommand command, CancellationToken cancellationToken)
        => Created(await _mediator.Send(command, cancellationToken), "You're subscribed.");

    private IActionResult Created(FormReceiptDto receipt, string message)
        => StatusCode(StatusCodes.Status201Created, ApiEnvelope.Success(receipt, message));
}

/// <summary>One inbox for support tickets, trade-ins, corporate inquiries, feedback and subscribers.</summary>
[Route("api/v{version:apiVersion}/admin/inbox")]
[Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
public sealed class AdminInboxController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public AdminInboxController(IMediator mediator) => _mediator = mediator;

    [HttpGet("{kind}")]
    public async Task<IActionResult> List(string kind, [FromQuery] int take = 50, CancellationToken cancellationToken = default)
        => Envelope(await _mediator.Send(new ListInboxQuery { Kind = kind, Take = take }, cancellationToken));

    [HttpPost("{kind}/{id:int}/status")]
    public async Task<IActionResult> ChangeStatus(
        string kind, int id, [FromBody] ChangeInboxStatusCommand command, CancellationToken cancellationToken)
    {
        command.Kind = kind;
        command.Id = id;
        await _mediator.Send(command, cancellationToken);
        return Envelope(new { id }, "Status updated.");
    }
}

/// <summary>Blog posts for the storefront.</summary>
public sealed class BlogsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public BlogsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("api/v{version:apiVersion}/blogs")]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 9, CancellationToken cancellationToken = default)
        => Envelope(await _mediator.Send(new RockyTelecom.Application.Features.Blogs.ListBlogsQuery { Page = page, PageSize = pageSize }, cancellationToken));

    [HttpGet("api/v{version:apiVersion}/blogs/{slug}")]
    public async Task<IActionResult> Get(string slug, CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new RockyTelecom.Application.Features.Blogs.GetBlogQuery { Slug = slug }, cancellationToken));

    [HttpGet("api/v{version:apiVersion}/store")]
    public async Task<IActionResult> Store(CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new RockyTelecom.Application.Features.Home.GetStorePublicQuery(), cancellationToken));
}

/// <summary>Product reviews: anyone can read approved ones; only verified buyers can write (FR-13).</summary>
public sealed class ProductReviewsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public ProductReviewsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("api/v{version:apiVersion}/products/{slug}/reviews")]
    public async Task<IActionResult> List(string slug, CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new RockyTelecom.Application.Features.Reviews.GetProductReviewsQuery { Slug = slug }, cancellationToken));

    [HttpGet("api/v{version:apiVersion}/products/{slug}/reviews/eligibility")]
    public async Task<IActionResult> Eligibility(string slug, CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new RockyTelecom.Application.Features.Reviews.GetReviewEligibilityQuery { Slug = slug }, cancellationToken));

    [HttpPost("api/v{version:apiVersion}/products/{slug}/reviews")]
    [Authorize]
    [EnableRateLimiting("forms")]
    public async Task<IActionResult> Submit(
        string slug, [FromBody] RockyTelecom.Application.Features.Reviews.SubmitReviewCommand command, CancellationToken cancellationToken)
    {
        command.Slug = slug;
        var id = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ApiEnvelope.Success(new { id }, "Thanks! Your review will appear once the team approves it."));
    }
}
