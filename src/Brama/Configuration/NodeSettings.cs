using Microsoft.Extensions.Logging;

namespace Brama.Configuration;

/// <summary>
/// The settings declared directly on a single config-tree node, before any cascade is applied.
/// Everything is nullable/empty by default so "not set here" is distinguishable from "set to the
/// default value here" — only the former inherits.
/// </summary>
internal sealed class NodeSettings
{
    public List<Type> Filters { get; } = [];

    public List<string> AuthorizationPolicies { get; } = [];

    public List<string> CorsPolicies { get; } = [];

    public List<string> RouteSegments { get; } = [];

    public int? Version { get; set; }

    public bool? Deprecated { get; set; }

    public DateOnly? Sunset { get; set; }

    public string? RateLimitPolicy { get; set; }

    public RateLimitPolicySpec? InlineRateLimit { get; set; }

    public bool? EmitRateLimitHeaders { get; set; }

    public TimeSpan? OutputCacheDuration { get; set; }

    public bool? DisallowLinks { get; set; }

    public bool? LogRequests { get; set; }

    public LogLevel? LogLevel { get; set; }

    public string? CorrelationIdHeader { get; set; }

    public bool? AllowAnonymous { get; set; }

    public string? AcceptedBreakingChangeReason { get; set; }

    /// <summary>
    /// Names of <see cref="ResolvedConfig"/> properties this node clears before applying its own
    /// value — the <c>.Replace(c =&gt; c.Field)</c> escape hatch out of the merge.
    /// </summary>
    public HashSet<string> ReplacedFields { get; } = new(StringComparer.Ordinal);
}
