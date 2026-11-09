namespace AqtCoreFW.Files.Application.Options;

/// <summary>Files module configuration — bound from appsettings "Files" section.</summary>
public sealed class FilesOptions
{
    public const string SectionName = "Files";

    /// <summary>Max upload size in MB (default 100 MB per spec; plan NF1 says 50 MB — use config).</summary>
    public int MaxFileSizeMb { get; init; } = 100;

    /// <summary>Days to retain quarantined files before cleanup.</summary>
    public int QuarantineDays { get; init; } = 7;

    /// <summary>MinIO bucket name for file storage.</summary>
    public string BucketName { get; init; } = "aqtcorefw-files";
}
