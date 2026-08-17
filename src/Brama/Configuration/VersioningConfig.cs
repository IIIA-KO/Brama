namespace Brama.Configuration;

/// <summary>
/// Root-level versioning settings. Chosen once for the whole API, not per group — a single API
/// speaking two different versioning dialects is a bug, not a feature.
/// </summary>
public sealed class VersioningConfig
{
    internal VersioningConfig()
    {
    }

    /// <summary>
    /// How versions are expressed, or <see langword="null"/> when versioning was never turned on —
    /// in which case routes are emitted exactly as written.
    /// </summary>
    public IVersionStrategy? Strategy { get; private set; }

    /// <summary>Puts the version in the path as <c>/v{n}/…</c>.</summary>
    public VersioningConfig UseUrlSegment()
    {
        Strategy = new UrlSegmentVersionStrategy();

        return this;
    }

    /// <summary>Uses a custom strategy.</summary>
    public VersioningConfig UseStrategy(IVersionStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        Strategy = strategy;

        return this;
    }
}
