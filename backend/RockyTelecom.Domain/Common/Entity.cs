namespace RockyTelecom.Domain.Common;

/// <summary>Base type for every persisted entity. PK is int identity unless noted.</summary>
public abstract class BaseEntity
{
    public int Id { get; set; }
}

/// <summary>Entity carrying UTC audit timestamps.</summary>
public abstract class AuditableEntity : BaseEntity
{
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
