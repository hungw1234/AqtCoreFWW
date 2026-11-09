namespace AqtCoreFW.Files.Infrastructure.Options;

/// <summary>MinIO connection options — bound from appsettings "MinIO" section.</summary>
public sealed class MinioOptions
{
    public const string SectionName = "MinIO";

    public string Endpoint { get; init; } = "localhost:9000";
    public string AccessKey { get; init; } = "minioadmin";
    public string SecretKey { get; init; } = "minioadmin";
    public string BucketName { get; init; } = "aqtcorefw-files";
    public bool UseSSL { get; init; } = false;
}
