namespace Brama.Configuration;

/// <summary>
/// A named, nestable node of the config tree. Created via <see cref="ConfigNode.Group"/> rather
/// than constructed directly, so a group always knows its parent.
/// </summary>
public sealed class EndpointGroup : ConfigNode<EndpointGroup>
{
    internal EndpointGroup(ConfigNode parent, string name)
        : base(parent)
    {
        Name = name;
        Settings.RouteSegments.Add(ToRouteSegment(name));
    }

    /// <summary>The group's name, used for OpenAPI tagging and as its contract-file identity.</summary>
    public string Name { get; }

    /// <summary>
    /// Overrides the route segment this group contributes, which defaults to its lower-cased
    /// <see cref="Name"/>. Useful when two groups must share a route but not a name — a versioned
    /// pair such as <c>Orders</c> and <c>OrdersV2</c> both serving <c>/orders</c>.
    /// </summary>
    /// <remarks>
    /// This replaces only <em>this</em> group's own segment; ancestors still contribute theirs.
    /// Calling it twice keeps the last call. To drop the ancestors' segments too — mounting a
    /// subtree at the API root — use <c>Replace(c =&gt; c.RouteSegments)</c>.
    /// </remarks>
    public EndpointGroup BaseRoute(string segment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        Settings.RouteSegments.Clear();
        Settings.RouteSegments.Add(segment.Trim('/'));

        return this;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "Lower case is the URL convention this segment is destined for, not a security normalization.")]
    private static string ToRouteSegment(string name) => name.ToLowerInvariant();
}
