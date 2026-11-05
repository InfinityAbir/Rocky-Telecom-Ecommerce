using FluentValidation;
using MediatR;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Application.Common.Models;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Constants;

namespace RockyTelecom.Application.Features.Admin.Users;

public sealed record AdminUserDto(
    string Id,
    string Name,
    string Phone,
    string? Email,
    bool PhoneVerified,
    bool IsBlocked,
    DateTime? LockoutEndUtc,
    DateTime CreatedAt,
    IReadOnlyList<string> Roles);

/// <summary>Paged user directory for /admin/users (CONVENTIONS §7).</summary>
public sealed class ListUsersQuery : IQuery<PagedResult<AdminUserDto>>
{
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed class ListUsersQueryHandler : IRequestHandler<ListUsersQuery, PagedResult<AdminUserDto>>
{
    private readonly IUserAccountService _accounts;

    public ListUsersQueryHandler(IUserAccountService accounts) => _accounts = accounts;

    public async Task<PagedResult<AdminUserDto>> Handle(ListUsersQuery request, CancellationToken cancellationToken)
    {
        var page = await _accounts.ListAsync(request.Search, request.Page, request.PageSize, cancellationToken);

        var items = new List<AdminUserDto>(page.Items.Count);

        foreach (var user in page.Items)
        {
            var roles = await _accounts.GetRolesAsync(user.Id, cancellationToken);
            items.Add(new AdminUserDto(
                user.Id,
                user.Name,
                user.Phone,
                user.Email,
                user.IsPhoneVerified,
                user.IsBlocked,
                user.LockoutEndUtc,
                user.CreatedAt,
                roles));
        }

        return new PagedResult<AdminUserDto>(items, page.Total, page.Page, page.PageSize);
    }
}

/// <summary>Blocks or unblocks an account; blocking also revokes its refresh token (D-10).</summary>
public sealed class SetUserBlockedCommand : ICommand<Unit>
{
    public string UserId { get; init; } = string.Empty;
    public bool Blocked { get; init; }
}

public sealed class SetUserBlockedCommandValidator : AbstractValidator<SetUserBlockedCommand>
{
    public SetUserBlockedCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}

public sealed class SetUserBlockedCommandHandler : IRequestHandler<SetUserBlockedCommand, Unit>
{
    private readonly IUserAccountService _accounts;
    private readonly ICurrentUser _currentUser;

    public SetUserBlockedCommandHandler(IUserAccountService accounts, ICurrentUser currentUser)
    {
        _accounts = accounts;
        _currentUser = currentUser;
    }

    public async Task<Unit> Handle(SetUserBlockedCommand request, CancellationToken cancellationToken)
    {
        // An admin must not lock themselves out of the admin area.
        if (string.Equals(_currentUser.UserId, request.UserId, StringComparison.Ordinal) && request.Blocked)
        {
            throw new ForbiddenException("You cannot block your own account.");
        }

        var result = await _accounts.SetBlockedAsync(request.UserId, request.Blocked, cancellationToken);

        if (!result.Succeeded)
        {
            throw new NotFoundException("Account", request.UserId);
        }

        return Unit.Value;
    }
}

/// <summary>Assigns one of the four system roles (ARCHITECTURE §7).</summary>
public sealed class AssignRoleCommand : ICommand<Unit>
{
    public string UserId { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
}

public sealed class AssignRoleCommandValidator : AbstractValidator<AssignRoleCommand>
{
    private static readonly string[] AllowedRoles =
    {
        Roles.Admin, Roles.Staff, Roles.Editor, Roles.Customer
    };

    public AssignRoleCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Role).NotEmpty().Must(role => AllowedRoles.Contains(role))
            .WithMessage($"Role must be one of: {string.Join(", ", AllowedRoles)}.");
    }
}

public sealed class AssignRoleCommandHandler : IRequestHandler<AssignRoleCommand, Unit>
{
    private readonly IUserAccountService _accounts;

    public AssignRoleCommandHandler(IUserAccountService accounts) => _accounts = accounts;

    public async Task<Unit> Handle(AssignRoleCommand request, CancellationToken cancellationToken)
    {
        var result = await _accounts.AddToRoleAsync(request.UserId, request.Role, cancellationToken);

        if (!result.Succeeded)
        {
            throw new DomainValidationException(
                "Could not assign the role.",
                new Dictionary<string, string[]> { ["role"] = result.Errors.ToArray() });
        }

        return Unit.Value;
    }
}
