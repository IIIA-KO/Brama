namespace Brama.Configuration;

/// <summary>
/// Decides how an API version is expressed on the wire.
/// </summary>
/// <remarks>
/// The interface exists in the MVP so that adding header- or media-type-based strategies later is
/// not a breaking change, but only <see cref="UrlSegmentVersionStrategy"/> ships now.
/// </remarks>
public interface IVersionStrategy
{
    /// <summary>
    /// Rewrites a route template for a given version. Returning the template unchanged means the
    /// version is carried somewhere other than the path.
    /// </summary>
    string ApplyVersion(string routeTemplate, int version);
}

/// <summary>
/// Puts the version in the path as <c>/v{n}/…</c>. Cache-friendly, trivially reproducible with
/// curl, and — because it bakes into the route template at build time — needs no per-request
/// version lookup.
/// </summary>
public sealed class UrlSegmentVersionStrategy : IVersionStrategy
{
    /// <inheritdoc />
    public string ApplyVersion(string routeTemplate, int version)
    {
        ArgumentNullException.ThrowIfNull(routeTemplate);

        return $"/v{version.ToString(System.Globalization.CultureInfo.InvariantCulture)}/{routeTemplate.TrimStart('/')}";
    }
}
