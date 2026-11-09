namespace AqtCoreFW.Files.Domain.Entities;

/// <summary>Strong-typed ID for FileRecord aggregate — prevents ID confusion across modules.</summary>
public readonly record struct FileId(Guid Value)
{
    public static FileId New() => new(Guid.NewGuid());
    public static FileId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
