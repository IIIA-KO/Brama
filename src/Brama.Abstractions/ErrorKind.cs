namespace Brama;

/// <summary>
/// The closed set of error categories a Brama handler can return.
/// </summary>
/// <remarks>
/// This enum is deliberately closed. Every member must have an HTTP status code mapped in
/// <c>ApiConfig.ProblemDetails(...)</c>; a missing mapping is reported at build time as
/// <c>BRAMA001</c> rather than falling through to a wrong status code at runtime.
/// </remarks>
public enum ErrorKind
{
    /// <summary>A request failed structural or business validation.</summary>
    Validation,

    /// <summary>The addressed resource does not exist.</summary>
    NotFound,

    /// <summary>The request conflicts with the current state of the resource.</summary>
    Conflict,

    /// <summary>The caller is not authenticated.</summary>
    Unauthorized,

    /// <summary>The caller is authenticated but not permitted to perform this operation.</summary>
    Forbidden,

    /// <summary>A problem that does not fit any other category.</summary>
    Problem,
}
