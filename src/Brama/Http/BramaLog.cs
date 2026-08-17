using Microsoft.Extensions.Logging;

namespace Brama.Http;

/// <summary>
/// Brama's own log messages, defined through the logging source generator so no allocation or
/// formatting happens for a request whose level is disabled.
/// </summary>
internal static partial class BramaLog
{
    [LoggerMessage(
        EventId = 1000,
        Message = "{Method} {Route} responded {StatusCode} in {ElapsedMilliseconds}ms (version {Version})")]
    public static partial void Request(
        ILogger logger,
        LogLevel level,
        string method,
        string route,
        int statusCode,
        double elapsedMilliseconds,
        int? version);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Links were attached to {Path}, but this subtree is configured with DisallowLinks. They were dropped from the response.")]
    public static partial void LinksSuppressed(ILogger logger, string path);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Links were attached to {Path}, but its response body serializes to {JsonKind} rather than an object, which has nowhere to put a _links member. They were dropped from the response.")]
    public static partial void LinksNeedAnObjectBody(ILogger logger, string path, string jsonKind);
}
