using Microsoft.Extensions.Logging;

namespace Brama.Configuration;

/// <summary>
/// One config-tree node's settings after the root-to-leaf cascade has been applied — the flat
/// value the runtime host and the source generator both work against.
/// </summary>
/// <remarks>
/// Property names on this type are the identity used by <c>ConfigNode.Replace(c =&gt; c.Field)</c>,
/// so renaming one silently changes which field an existing <c>Replace</c> call clears. Keep them
/// in lockstep with <see cref="NodeSettings"/>.
/// </remarks>
public sealed record ResolvedConfig
{
    /// <summary>Endpoint filter types, outermost (root) first. List-valued: appends down the cascade.</summary>
    public IReadOnlyList<Type> Filters { get; init; } = [];

    /// <summary>ASP.NET Core authorization policy names, all of which must be satisfied. List-valued: appends.</summary>
    public IReadOnlyList<string> AuthorizationPolicies { get; init; } = [];

    /// <summary>Named CORS policies to apply. List-valued: appends.</summary>
    public IReadOnlyList<string> CorsPolicies { get; init; } = [];

    /// <summary>Route prefix segments contributed by each group on the path, root first. List-valued: appends.</summary>
    public IReadOnlyList<string> RouteSegments { get; init; } = [];

    /// <summary>The API version this node belongs to, if versioning is in use. Scalar: replaces.</summary>
    public int? Version { get; init; }

    /// <summary>Whether this node's endpoints are marked deprecated (RFC 8594). Scalar: replaces.</summary>
    public bool Deprecated { get; init; }

    /// <summary>The RFC 8594 sunset date for this node's endpoints. Scalar: replaces.</summary>
    public DateOnly? Sunset { get; init; }

    /// <summary>An externally registered rate-limiting policy name. Scalar: replaces.</summary>
    public string? RateLimitPolicy { get; init; }

    /// <summary>A rate-limit policy authored inline in the tree. Scalar: replaces.</summary>
    public RateLimitPolicySpec? InlineRateLimit { get; init; }

    /// <summary>Whether to emit informational <c>RateLimit-*</c> response headers. Scalar: replaces.</summary>
    public bool EmitRateLimitHeaders { get; init; }

    /// <summary>Output-cache duration, if output caching is enabled here. Scalar: replaces.</summary>
    public TimeSpan? OutputCacheDuration { get; init; }

    /// <summary>Whether hypermedia links are forbidden in this subtree. Scalar: replaces.</summary>
    public bool DisallowLinks { get; init; }

    /// <summary>Whether one structured log entry is emitted per request. Scalar: replaces.</summary>
    public bool LogRequests { get; init; }

    /// <summary>The level request-logging entries are written at. Scalar: replaces.</summary>
    public LogLevel? LogLevel { get; init; }

    /// <summary>The header a correlation ID is read from and echoed on, if propagation is enabled. Scalar: replaces.</summary>
    public string? CorrelationIdHeader { get; init; }

    /// <summary>Whether authentication is waived for this node's endpoints. Scalar: replaces.</summary>
    public bool AllowAnonymous { get; init; }

    /// <summary>
    /// The reason given for deliberately breaking an already-frozen version's contract, if any.
    /// Scalar: replaces. See the Contracts feature.
    /// </summary>
    public string? AcceptedBreakingChangeReason { get; init; }
}
