namespace Brama;

/// <summary>
/// What a handler is given to reach the request, resolve services, and build its result. Bound as
/// an ordinary handler parameter.
/// </summary>
public interface IEndpointContext : IErrorContext
{
    /// <summary>
    /// The path of a named endpoint. Backs the generated <c>Links</c> methods; the group prefix and
    /// version segment are applied by ASP.NET Core's own link generation, because they come from the
    /// config tree and so are not known until the application is running.
    /// </summary>
    /// <param name="routeName">The name given to the endpoint with <c>Named(...)</c>.</param>
    /// <param name="routeValues">Values for the route's parameters.</param>
    string PathTo(string routeName, object? routeValues = null);

    /// <summary>Resolves a service from the request scope.</summary>
    TService Get<TService>()
        where TService : notnull;

    /// <summary>A 200 response carrying <paramref name="value"/>.</summary>
    Result<T> Ok<T>(T value);

    /// <summary>
    /// A 201 response whose <c>Location</c> header is this request's path with
    /// <paramref name="id"/> appended — the common case, where a POST to a collection creates a
    /// member of it.
    /// </summary>
    Result<T> Created<T>(T value, object id);

    /// <summary>A 201 response with an explicit <c>Location</c> header.</summary>
    Result<T> CreatedAt<T>(T value, string location);

    /// <summary>A 204 response with no body.</summary>
    Result<T> NoContent<T>();

    /// <summary>A <see cref="ErrorKind.NotFound"/> failure.</summary>
    ErrorResult NotFound(string? detail = null);

    /// <summary>A <see cref="ErrorKind.Conflict"/> failure.</summary>
    ErrorResult Conflict(string? detail = null);

    /// <summary>A <see cref="ErrorKind.Forbidden"/> failure.</summary>
    ErrorResult Forbidden(string? detail = null);

    /// <summary>A <see cref="ErrorKind.Unauthorized"/> failure.</summary>
    ErrorResult Unauthorized(string? detail = null);

    /// <summary>A failure of any kind.</summary>
    ErrorResult Error(ErrorKind kind, string detail);

    /// <summary>A validation failure carrying per-field problems.</summary>
    ErrorResult Invalid(string detail, IReadOnlyList<ValidationFailure> failures);
}
