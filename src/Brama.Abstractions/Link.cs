namespace Brama;

/// <summary>
/// One hypermedia link on a response, serialized into the <c>_links</c> extension member.
/// </summary>
/// <param name="Rel">The link relation, e.g. <c>self</c> or <c>cancel</c>.</param>
/// <param name="Href">Where the relation points.</param>
public sealed record Link(string Rel, string Href);
