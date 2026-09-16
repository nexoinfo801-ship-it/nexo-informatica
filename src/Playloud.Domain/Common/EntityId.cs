namespace Playloud.Domain.Common;

public readonly record struct EntityId<T>(Guid Value)
{
    public static EntityId<T> New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}
