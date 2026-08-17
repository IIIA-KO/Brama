using Microsoft.AspNetCore.Http;

namespace Brama;

/// <summary>
/// The context handed to an unhandled-exception callback when Brama is deciding what
/// <c>detail</c> string to put on a problem+json response.
/// </summary>
public interface IErrorContext
{
    /// <summary>The HTTP context of the request that faulted.</summary>
    HttpContext HttpContext { get; }

    /// <summary>
    /// Whether the host is running in the Development environment. Exposed so a callback can opt
    /// in to leaking exception detail there; Brama never does so on its own.
    /// </summary>
    bool IsDevelopment { get; }
}
