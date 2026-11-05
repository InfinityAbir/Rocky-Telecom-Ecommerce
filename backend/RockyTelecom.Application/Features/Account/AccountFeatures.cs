using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Entities;
using RockyTelecom.Domain.ValueObjects;

namespace RockyTelecom.Application.Features.Account;

public sealed record AddressDto(
    int Id,
    string Name,
    string Phone,
    string Division,
    string District,
    string Thana,
    string Line,
    bool IsDefault);

public sealed class GetAddressesQuery : IQuery<IReadOnlyList<AddressDto>>;

public sealed class GetAddressesQueryHandler : IRequestHandler<GetAddressesQuery, IReadOnlyList<AddressDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetAddressesQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<AddressDto>> Handle(GetAddressesQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedException("Sign in to manage addresses.");

        return await _db.Addresses
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.Id)
            .Select(a => new AddressDto(a.Id, a.Name, a.Phone, a.Division, a.District, a.Thana, a.Line, a.IsDefault))
            .ToListAsync(cancellationToken);
    }
}

public sealed class CreateAddressCommand : ICommand<int>
{
    public string Name { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string Division { get; init; } = string.Empty;
    public string District { get; init; } = string.Empty;
    public string Thana { get; init; } = string.Empty;
    public string Line { get; init; } = string.Empty;
    public bool IsDefault { get; init; }
}

public sealed class AddressInputValidator : AbstractValidator<CreateAddressCommand>
{
    public AddressInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Phone).NotEmpty().Must(Phone.IsValidFormat)
            .WithMessage("Enter a valid Bangladeshi mobile number (01XXXXXXXXX).");
        RuleFor(x => x.Division).NotEmpty().MaximumLength(60);
        RuleFor(x => x.District).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Thana).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Line).NotEmpty().MaximumLength(300);
    }
}

public sealed class CreateAddressCommandHandler : IRequestHandler<CreateAddressCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public CreateAddressCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<int> Handle(CreateAddressCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedException("Sign in to manage addresses.");

        var hasAny = await _db.Addresses.AnyAsync(a => a.UserId == userId, cancellationToken);
        var makeDefault = request.IsDefault || !hasAny;

        if (makeDefault)
        {
            await ClearDefaultsAsync(userId, cancellationToken);
        }

        var address = new Address
        {
            UserId = userId,
            Name = request.Name.Trim(),
            Phone = Phone.Create(request.Phone).Normalised,
            Division = request.Division.Trim(),
            District = request.District.Trim(),
            Thana = request.Thana.Trim(),
            Line = request.Line.Trim(),
            IsDefault = makeDefault,
            CreatedAt = _clock.UtcNow
        };

        _db.Addresses.Add(address);
        await _db.SaveChangesAsync(cancellationToken);

        return address.Id;
    }

    private async Task ClearDefaultsAsync(string userId, CancellationToken cancellationToken)
    {
        var current = await _db.Addresses
            .Where(a => a.UserId == userId && a.IsDefault)
            .ToListAsync(cancellationToken);

        foreach (var address in current)
        {
            address.IsDefault = false;
        }
    }
}

public sealed class UpdateAddressCommand : ICommand<Unit>
{
    /// <summary>Settable so the API can bind it from the route.</summary>
    public int Id { get; set; }
    public string Name { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string Division { get; init; } = string.Empty;
    public string District { get; init; } = string.Empty;
    public string Thana { get; init; } = string.Empty;
    public string Line { get; init; } = string.Empty;
    public bool IsDefault { get; init; }
}

public sealed class UpdateAddressCommandValidator : AbstractValidator<UpdateAddressCommand>
{
    public UpdateAddressCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Phone).NotEmpty().Must(Phone.IsValidFormat)
            .WithMessage("Enter a valid Bangladeshi mobile number (01XXXXXXXXX).");
        RuleFor(x => x.Division).NotEmpty().MaximumLength(60);
        RuleFor(x => x.District).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Thana).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Line).NotEmpty().MaximumLength(300);
    }
}

public sealed class UpdateAddressCommandHandler : IRequestHandler<UpdateAddressCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public UpdateAddressCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Unit> Handle(UpdateAddressCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedException("Sign in to manage addresses.");

        var address = await _db.Addresses
            .FirstOrDefaultAsync(a => a.Id == request.Id && a.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Address", request.Id);

        if (request.IsDefault)
        {
            var others = await _db.Addresses
                .Where(a => a.UserId == userId && a.Id != address.Id && a.IsDefault)
                .ToListAsync(cancellationToken);

            foreach (var other in others)
            {
                other.IsDefault = false;
            }
        }

        address.Name = request.Name.Trim();
        address.Phone = Phone.Create(request.Phone).Normalised;
        address.Division = request.Division.Trim();
        address.District = request.District.Trim();
        address.Thana = request.Thana.Trim();
        address.Line = request.Line.Trim();
        address.IsDefault = request.IsDefault;
        address.UpdatedAt = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed class DeleteAddressCommand : ICommand<Unit>
{
    public int Id { get; init; }
}

public sealed class DeleteAddressCommandHandler : IRequestHandler<DeleteAddressCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public DeleteAddressCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Unit> Handle(DeleteAddressCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedException("Sign in to manage addresses.");

        var address = await _db.Addresses
            .FirstOrDefaultAsync(a => a.Id == request.Id && a.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Address", request.Id);

        _db.Addresses.Remove(address);
        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

// ---------------------------------------------------------------------------------------------
// Profile
// ---------------------------------------------------------------------------------------------

public sealed record ProfileDto(
    string Id,
    string Name,
    string Phone,
    string? Email,
    bool PhoneVerified,
    IReadOnlyList<string> Roles);

public sealed class GetProfileQuery : IQuery<ProfileDto>;

public sealed class GetProfileQueryHandler : IRequestHandler<GetProfileQuery, ProfileDto>
{
    private readonly IUserAccountService _accounts;
    private readonly ICurrentUser _currentUser;

    public GetProfileQueryHandler(IUserAccountService accounts, ICurrentUser currentUser)
    {
        _accounts = accounts;
        _currentUser = currentUser;
    }

    public async Task<ProfileDto> Handle(GetProfileQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedException("Not signed in.");

        var user = await _accounts.FindByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("Account", userId);

        var roles = await _accounts.GetRolesAsync(userId, cancellationToken);

        return new ProfileDto(user.Id, user.Name, user.Phone, user.Email, user.IsPhoneVerified, roles);
    }
}

public sealed class UpdateProfileCommand : ICommand<Unit>
{
    public string Name { get; init; } = string.Empty;
    public string? Email { get; init; }
}

public sealed class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

public sealed class UpdateProfileCommandHandler : IRequestHandler<UpdateProfileCommand, Unit>
{
    private readonly IUserAccountService _accounts;
    private readonly ICurrentUser _currentUser;

    public UpdateProfileCommandHandler(IUserAccountService accounts, ICurrentUser currentUser)
    {
        _accounts = accounts;
        _currentUser = currentUser;
    }

    public async Task<Unit> Handle(UpdateProfileCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedException("Not signed in.");

        var result = await _accounts.UpdateProfileAsync(userId, request.Name, request.Email, cancellationToken);

        if (!result.Succeeded)
        {
            throw new DomainValidationException(
                "Could not update the profile.",
                new Dictionary<string, string[]> { ["profile"] = result.Errors.ToArray() });
        }

        return Unit.Value;
    }
}

public sealed class ChangePasswordCommand : ICommand<Unit>
{
    public string CurrentPassword { get; init; } = string.Empty;
    public string NewPassword { get; init; } = string.Empty;
}

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128)
            .NotEqual(x => x.CurrentPassword).WithMessage("Choose a password different from the current one.");
    }
}

public sealed class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, Unit>
{
    private readonly IUserAccountService _accounts;
    private readonly ICurrentUser _currentUser;

    public ChangePasswordCommandHandler(IUserAccountService accounts, ICurrentUser currentUser)
    {
        _accounts = accounts;
        _currentUser = currentUser;
    }

    public async Task<Unit> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedException("Not signed in.");

        var result = await _accounts.ChangePasswordAsync(
            userId, request.CurrentPassword, request.NewPassword, cancellationToken);

        if (!result.Succeeded)
        {
            throw new DomainValidationException(
                "Could not change the password.",
                new Dictionary<string, string[]> { ["currentPassword"] = result.Errors.ToArray() });
        }

        return Unit.Value;
    }
}
