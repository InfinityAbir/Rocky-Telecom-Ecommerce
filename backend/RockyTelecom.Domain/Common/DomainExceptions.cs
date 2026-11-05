namespace RockyTelecom.Domain.Common;

/// <summary>Base for all deliberate domain failures. Mapped to the API envelope by middleware.</summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
    protected DomainException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Requested resource does not exist. → HTTP 404.</summary>
public sealed class NotFoundException : DomainException
{
    public NotFoundException(string message) : base(message) { }
    public NotFoundException(string entity, object key) : base($"{entity} '{key}' was not found.") { }
}

/// <summary>Input violates a business rule. → HTTP 400 with a field map where available.</summary>
public sealed class DomainValidationException : DomainException
{
    public DomainValidationException(string message, IReadOnlyDictionary<string, string[]>? errors = null)
        : base(message)
    {
        Errors = errors ?? new Dictionary<string, string[]>();
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>State conflict, e.g. insufficient stock or duplicate unique key. → HTTP 409.</summary>
public sealed class ConflictException : DomainException
{
    public ConflictException(string message) : base(message) { }
}

/// <summary>Authenticated but not permitted. → HTTP 403.</summary>
public sealed class ForbiddenException : DomainException
{
    public ForbiddenException(string message) : base(message) { }
}

/// <summary>Missing/invalid credentials. → HTTP 401.</summary>
public sealed class UnauthorizedException : DomainException
{
    public UnauthorizedException(string message) : base(message) { }
}
