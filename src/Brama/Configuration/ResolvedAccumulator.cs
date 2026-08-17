using Microsoft.Extensions.Logging;

namespace Brama.Configuration;

/// <summary>
/// Folds a root-to-leaf chain of <see cref="NodeSettings"/> into one <see cref="ResolvedConfig"/>.
/// </summary>
/// <remarks>
/// Each node is applied in order. A field named in <see cref="NodeSettings.ReplacedFields"/> is
/// reset to its inherit-nothing state <em>before</em> that same node's own value is applied, which
/// is what makes <c>.Replace(c =&gt; c.Filters).UseFilter&lt;X&gt;()</c> mean "only X here".
/// </remarks>
internal sealed class ResolvedAccumulator
{
    private readonly List<Type> _filters = [];
    private readonly List<string> _authorizationPolicies = [];
    private readonly List<string> _corsPolicies = [];
    private readonly List<string> _routeSegments = [];

    private int? _version;
    private bool _deprecated;
    private DateOnly? _sunset;
    private string? _rateLimitPolicy;
    private RateLimitPolicySpec? _inlineRateLimit;
    private bool _emitRateLimitHeaders;
    private TimeSpan? _outputCacheDuration;
    private bool _disallowLinks;
    private bool _logRequests;
    private LogLevel? _logLevel;
    private string? _correlationIdHeader;
    private bool _allowAnonymous;
    private string? _acceptedBreakingChangeReason;

    public void Apply(NodeSettings settings)
    {
        ApplyList(settings, nameof(ResolvedConfig.Filters), _filters, settings.Filters);
        ApplyList(settings, nameof(ResolvedConfig.AuthorizationPolicies), _authorizationPolicies, settings.AuthorizationPolicies);
        ApplyList(settings, nameof(ResolvedConfig.CorsPolicies), _corsPolicies, settings.CorsPolicies);
        ApplyList(settings, nameof(ResolvedConfig.RouteSegments), _routeSegments, settings.RouteSegments);

        _version = Scalar(settings, nameof(ResolvedConfig.Version), _version, settings.Version);
        _sunset = Scalar(settings, nameof(ResolvedConfig.Sunset), _sunset, settings.Sunset);
        _rateLimitPolicy = Scalar(settings, nameof(ResolvedConfig.RateLimitPolicy), _rateLimitPolicy, settings.RateLimitPolicy);
        _inlineRateLimit = Scalar(settings, nameof(ResolvedConfig.InlineRateLimit), _inlineRateLimit, settings.InlineRateLimit);
        _outputCacheDuration = Scalar(settings, nameof(ResolvedConfig.OutputCacheDuration), _outputCacheDuration, settings.OutputCacheDuration);
        _logLevel = Scalar(settings, nameof(ResolvedConfig.LogLevel), _logLevel, settings.LogLevel);
        _correlationIdHeader = Scalar(settings, nameof(ResolvedConfig.CorrelationIdHeader), _correlationIdHeader, settings.CorrelationIdHeader);
        _acceptedBreakingChangeReason = Scalar(
            settings,
            nameof(ResolvedConfig.AcceptedBreakingChangeReason),
            _acceptedBreakingChangeReason,
            settings.AcceptedBreakingChangeReason);

        _deprecated = Flag(settings, nameof(ResolvedConfig.Deprecated), _deprecated, settings.Deprecated);
        _emitRateLimitHeaders = Flag(settings, nameof(ResolvedConfig.EmitRateLimitHeaders), _emitRateLimitHeaders, settings.EmitRateLimitHeaders);
        _disallowLinks = Flag(settings, nameof(ResolvedConfig.DisallowLinks), _disallowLinks, settings.DisallowLinks);
        _logRequests = Flag(settings, nameof(ResolvedConfig.LogRequests), _logRequests, settings.LogRequests);
        _allowAnonymous = Flag(settings, nameof(ResolvedConfig.AllowAnonymous), _allowAnonymous, settings.AllowAnonymous);
    }

    public ResolvedConfig ToResolvedConfig() => new()
    {
        Filters = [.. _filters],
        AuthorizationPolicies = [.. _authorizationPolicies],
        CorsPolicies = [.. _corsPolicies],
        RouteSegments = [.. _routeSegments],
        Version = _version,
        Deprecated = _deprecated,
        Sunset = _sunset,
        RateLimitPolicy = _rateLimitPolicy,
        InlineRateLimit = _inlineRateLimit,
        EmitRateLimitHeaders = _emitRateLimitHeaders,
        OutputCacheDuration = _outputCacheDuration,
        DisallowLinks = _disallowLinks,
        LogRequests = _logRequests,
        LogLevel = _logLevel,
        CorrelationIdHeader = _correlationIdHeader,
        AllowAnonymous = _allowAnonymous,
        AcceptedBreakingChangeReason = _acceptedBreakingChangeReason,
    };

    private static void ApplyList<T>(NodeSettings settings, string field, List<T> accumulated, List<T> declared)
    {
        if (settings.ReplacedFields.Contains(field))
        {
            accumulated.Clear();
        }

        accumulated.AddRange(declared);
    }

    private static T? Scalar<T>(NodeSettings settings, string field, T? accumulated, T? declared)
    {
        T? current = settings.ReplacedFields.Contains(field) ? default : accumulated;

        return declared is null ? current : declared;
    }

    private static bool Flag(NodeSettings settings, string field, bool accumulated, bool? declared)
    {
        bool current = !settings.ReplacedFields.Contains(field) && accumulated;

        return declared ?? current;
    }
}
