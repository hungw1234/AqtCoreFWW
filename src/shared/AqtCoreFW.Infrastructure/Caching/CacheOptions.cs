namespace AqtCoreFW.Infrastructure.Caching;

/// <summary>
/// Cache configuration options — bound from appsettings "Cache" section.
/// Mode: Memory | TwoTier (default). TwoTier = L1 IMemoryCache + L2 Redis.
/// </summary>
public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    public string Mode { get; set; } = "TwoTier";
    public L1Options L1 { get; set; } = new();
    public L2Options L2 { get; set; } = new();

    public sealed class L1Options
    {
        public int DefaultTtlMinutes { get; set; } = 10;
        public int MaxEntries { get; set; } = 5000;
    }

    public sealed class L2Options
    {
        public int DefaultTtlMinutes { get; set; } = 60;
        public int JitterPercent { get; set; } = 20;
    }
}
