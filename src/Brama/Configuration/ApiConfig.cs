namespace Brama.Configuration;

/// <summary>
/// The root of a Brama config tree: one per application. Everything declared here is inherited by
/// every group and endpoint beneath it.
/// </summary>
public sealed class ApiConfig : ConfigNode<ApiConfig>
{
    /// <summary>Creates an empty root node.</summary>
    public ApiConfig()
        : base(parent: null)
    {
    }

    /// <summary>The error-to-status mapping table shared by the entire API.</summary>
    public ProblemDetailsConfig ProblemDetailsConfig { get; } = new();

    /// <summary>How API versions are expressed, if versioning is in use at all.</summary>
    public VersioningConfig VersioningConfig { get; } = new();

    /// <summary>Configures the API-wide error-to-status mapping table.</summary>
    public ApiConfig ProblemDetails(Action<ProblemDetailsConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(ProblemDetailsConfig);

        return this;
    }

    /// <summary>
    /// Turns on API versioning and picks how versions are expressed. Leave this uncalled and every
    /// route is emitted exactly as written, with no version prefix and no behaviour change.
    /// </summary>
    public ApiConfig Versioning(Action<VersioningConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(VersioningConfig);

        return this;
    }
}
