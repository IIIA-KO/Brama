using System.Linq.Expressions;
using Microsoft.AspNetCore.Http;

namespace Brama.Configuration;

/// <summary>
/// A node in Brama's configuration tree: <see cref="ApiConfig"/> at the root, nested
/// <see cref="EndpointGroup"/> instances beneath it, endpoints as leaves.
/// </summary>
/// <remarks>
/// Settings declared on a node cascade to its descendants. List-valued settings append down the
/// chain; scalar settings replace. <c>Replace(c =&gt; c.Field)</c> clears the inherited value of one
/// field before the declaring node's own value is applied.
/// </remarks>
public abstract partial class ConfigNode
{
    private readonly List<EndpointGroup> _children = [];

    private protected ConfigNode(ConfigNode? parent) => Parent = parent;

    /// <summary>The node this one inherits from, or <see langword="null"/> for the root.</summary>
    internal ConfigNode? Parent { get; }

    internal NodeSettings Settings { get; } = new();

    /// <summary>The groups declared directly beneath this node.</summary>
    public IReadOnlyList<EndpointGroup> Children => _children;

    /// <summary>
    /// Declares a nested group. The group inherits everything set on this node; whatever
    /// <paramref name="configure"/> sets is layered on top of that.
    /// </summary>
    /// <param name="name">
    /// The group's name. Also its default route segment, lower-cased, unless
    /// <see cref="EndpointGroup.BaseRoute"/> overrides it.
    /// </param>
    /// <param name="configure">Settings to declare on the new group.</param>
    public EndpointGroup Group(string name, Action<EndpointGroup>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var group = new EndpointGroup(this, name);
        _children.Add(group);
        configure?.Invoke(group);

        return group;
    }

    /// <summary>
    /// This node and everything below it — nested groups and the endpoints declared on each — in
    /// declaration order.
    /// </summary>
    public IEnumerable<ConfigNode> DescendantsAndSelf()
    {
        yield return this;

        foreach (EndpointDefinition endpoint in Endpoints)
        {
            yield return endpoint;
        }

        foreach (EndpointGroup child in _children)
        {
            foreach (ConfigNode node in child.DescendantsAndSelf())
            {
                yield return node;
            }
        }
    }

    /// <summary>Every endpoint in this subtree.</summary>
    public IEnumerable<EndpointDefinition> AllEndpoints() => DescendantsAndSelf().OfType<EndpointDefinition>();

    /// <summary>
    /// Walks this node's ancestry root-first and flattens it into a single value. Called once per
    /// node at startup — never per request.
    /// </summary>
    public ResolvedConfig Resolve()
    {
        List<ConfigNode> chain = [];
        for (ConfigNode? node = this; node is not null; node = node.Parent)
        {
            chain.Add(node);
        }

        chain.Reverse();

        var accumulator = new ResolvedAccumulator();
        foreach (ConfigNode node in chain)
        {
            accumulator.Apply(node.Settings);
        }

        return accumulator.ToResolvedConfig();
    }
}

/// <summary>
/// Adds the fluent, chainable settings API to <see cref="ConfigNode"/>, typed so that every call
/// returns the concrete node type rather than the base.
/// </summary>
/// <typeparam name="TSelf">The concrete node type, for chaining.</typeparam>
public abstract class ConfigNode<TSelf> : ConfigNode
    where TSelf : ConfigNode<TSelf>
{
    private protected ConfigNode(ConfigNode? parent)
        : base(parent)
    {
    }

    private TSelf Self => (TSelf)this;

    /// <summary>
    /// Clears whatever this node inherited for one field, before this node's own value for it is
    /// applied. Scoped to a single field — the rest of the cascade is untouched.
    /// </summary>
    /// <param name="field">
    /// A property of <see cref="ResolvedConfig"/>, e.g. <c>c =&gt; c.Filters</c>.
    /// </param>
    public TSelf Replace<TValue>(Expression<Func<ResolvedConfig, TValue>> field)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (field.Body is not MemberExpression member)
        {
            throw new ArgumentException(
                "Replace expects a direct property access on ResolvedConfig, for example c => c.Filters.",
                nameof(field));
        }

        Settings.ReplacedFields.Add(member.Member.Name);

        return Self;
    }

    /// <summary>Adds an endpoint filter to this subtree. Filters append down the cascade.</summary>
    /// <typeparam name="TFilter">The filter to run, outermost ancestors first.</typeparam>
    public TSelf UseFilter<TFilter>()
        where TFilter : IEndpointFilter
    {
        Settings.Filters.Add(typeof(TFilter));

        return Self;
    }

    /// <summary>
    /// Requires an ASP.NET Core authorization policy. Policies append down the cascade, so a
    /// subgroup's policy is required <em>in addition to</em> its ancestors', not instead of them.
    /// </summary>
    public TSelf RequireAuthorization(string policyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        Settings.AuthorizationPolicies.Add(policyName);

        return Self;
    }

    /// <summary>Waives authentication for this subtree.</summary>
    public TSelf AllowAnonymous()
    {
        Settings.AllowAnonymous = true;

        return Self;
    }

    /// <summary>Applies a named ASP.NET Core CORS policy. CORS policies append down the cascade.</summary>
    public TSelf Cors(string policyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        Settings.CorsPolicies.Add(policyName);

        return Self;
    }

    /// <summary>Assigns this subtree to an API version.</summary>
    public TSelf Version(int version)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        Settings.Version = version;

        return Self;
    }

    /// <summary>Marks this subtree deprecated, emitting an RFC 8594 <c>Deprecation</c> header.</summary>
    public TSelf Deprecated()
    {
        Settings.Deprecated = true;

        return Self;
    }

    /// <summary>Announces when this subtree stops being served, via an RFC 8594 <c>Sunset</c> header.</summary>
    public TSelf Sunset(DateOnly date)
    {
        Settings.Sunset = date;

        return Self;
    }

    /// <summary>Applies a rate-limiting policy that the host registered itself.</summary>
    public TSelf RequireRateLimiting(string policyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        Settings.RateLimitPolicy = policyName;

        return Self;
    }

    /// <summary>
    /// Authors a fixed-window rate-limit policy inline, so the limit lives next to the endpoints it
    /// protects. Brama registers the equivalent ASP.NET Core policy on the host's behalf.
    /// </summary>
    public TSelf RateLimit(int permitLimit, TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(permitLimit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        Settings.InlineRateLimit = new RateLimitPolicySpec(permitLimit, window);

        return Self;
    }

    /// <summary>Emits informational <c>RateLimit-Limit</c>/<c>-Remaining</c>/<c>-Reset</c> response headers.</summary>
    public TSelf EmitRateLimitHeaders()
    {
        Settings.EmitRateLimitHeaders = true;

        return Self;
    }

    /// <summary>Caches responses in this subtree via ASP.NET Core's output-caching middleware.</summary>
    public TSelf OutputCache(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        Settings.OutputCacheDuration = duration;

        return Self;
    }

    /// <summary>Forbids hypermedia links in this subtree, for teams enforcing that consistency.</summary>
    public TSelf DisallowLinks()
    {
        Settings.DisallowLinks = true;

        return Self;
    }

    /// <summary>Emits one structured log entry per request through this subtree.</summary>
    public TSelf LogRequests()
    {
        Settings.LogRequests = true;

        return Self;
    }

    /// <summary>Overrides the level this subtree's request-logging entries are written at.</summary>
    public TSelf LogLevel(Microsoft.Extensions.Logging.LogLevel level)
    {
        Settings.LogLevel = level;

        return Self;
    }

    /// <summary>
    /// Reads a correlation ID from <paramref name="headerName"/> (generating one when absent),
    /// scopes it onto the logger for the request, and echoes it on the response — including on
    /// error paths.
    /// </summary>
    public TSelf PropagateCorrelationId(string headerName = "X-Correlation-Id")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
        Settings.CorrelationIdHeader = headerName;

        return Self;
    }

    /// <summary>
    /// Permits one build to rewrite this version's frozen contract snapshot despite a breaking
    /// change. Transient by intent: it belongs in the same pull request as the change it explains,
    /// and is normally removed afterwards.
    /// </summary>
    /// <param name="reason">Why the break is acceptable. Ends up in the pull-request diff.</param>
    public TSelf AcceptBreakingChange(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Settings.AcceptedBreakingChangeReason = reason;

        return Self;
    }
}
