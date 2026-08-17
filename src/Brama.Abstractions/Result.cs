using System.Collections.Immutable;

namespace Brama;

/// <summary>
/// What a Brama handler returns: either a value and the success status it should be sent with, or
/// an <see cref="Brama.Error"/>. Brama turns it into an HTTP response, so handlers never write
/// <c>result.Match(Results.Ok, Problem)</c> themselves.
/// </summary>
/// <typeparam name="T">The response body type.</typeparam>
public readonly record struct Result<T>
{
    internal Result(bool isSuccess, T? value, Error? error, int successStatusCode, string? location, ImmutableArray<Link> links)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
        SuccessStatusCode = successStatusCode;
        Location = location;
        Links = links;
    }

    /// <summary>Whether the handler succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>The response body, when <see cref="IsSuccess"/>. Absent for 204 responses.</summary>
    public T? Value { get; }

    /// <summary>The failure, when not <see cref="IsSuccess"/>.</summary>
    public Error? Error { get; }

    /// <summary>The status code a successful response is sent with, e.g. 200, 201 or 204.</summary>
    public int SuccessStatusCode { get; }

    /// <summary>The <c>Location</c> header value for a 201 response.</summary>
    public string? Location { get; }

    /// <summary>Hypermedia links attached via <see cref="WithLink"/>. Empty unless explicitly added.</summary>
    public ImmutableArray<Link> Links { get; }

    /// <summary>Adopts a failure that has no response type of its own.</summary>
    public static implicit operator Result<T>(ErrorResult error) => FromErrorResult(error);

    /// <summary>
    /// Adopts a failure that has no response type of its own — the named alternate to the implicit
    /// conversion above, for languages without operator overloading.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1000:Do not declare static members on generic types",
        Justification = "Required shape: a conversion operator's named alternate must be static on the operator's declaring type.")]
    public static Result<T> FromErrorResult(ErrorResult error) =>
        new(isSuccess: false, default, error.Error, successStatusCode: 0, location: null, []);

    /// <summary>
    /// Attaches a hypermedia link. A response with no <see cref="WithLink"/> call serializes
    /// byte-identically to one from a library that has no hypermedia support at all.
    /// </summary>
    /// <param name="rel">The link relation, e.g. <c>self</c>.</param>
    /// <param name="href">Where the relation points.</param>
    /// <param name="when">
    /// Attach the link only if this holds — conditional hypermedia expressed as plain C# rather
    /// than a state-machine DSL.
    /// </param>
    public Result<T> WithLink(string rel, string href, bool when = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rel);
        ArgumentException.ThrowIfNullOrWhiteSpace(href);

        if (!when || !IsSuccess)
        {
            return this;
        }

        return new Result<T>(IsSuccess, Value, Error, SuccessStatusCode, Location, Links.Add(new Link(rel, href)));
    }
}

/// <summary>
/// Creates <see cref="Result{T}"/> values. A non-generic host so the factories can infer
/// <typeparamref name="T" /> from their argument at every call site.
/// </summary>
public static class Result
{
    /// <summary>A 200 response carrying <paramref name="value"/>.</summary>
    public static Result<T> Ok<T>(T value) =>
        new(isSuccess: true, value, error: null, successStatusCode: 200, location: null, []);

    /// <summary>A 201 response carrying <paramref name="value"/> and a <c>Location</c> header.</summary>
    public static Result<T> Created<T>(T value, string location)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);

        return new Result<T>(isSuccess: true, value, error: null, successStatusCode: 201, location, []);
    }

    /// <summary>A 204 response with no body.</summary>
    public static Result<T> NoContent<T>() =>
        new(isSuccess: true, default, error: null, successStatusCode: 204, location: null, []);

    /// <summary>A failure, to be mapped to a status code by the API's problem-details table.</summary>
    public static ErrorResult Failure(ErrorKind kind, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        return new ErrorResult(new Error(kind, detail));
    }

    /// <summary>A validation failure carrying per-field problems.</summary>
    public static ErrorResult Invalid(string detail, IReadOnlyList<ValidationFailure> failures)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        ArgumentNullException.ThrowIfNull(failures);

        return new ErrorResult(new Error(ErrorKind.Validation, detail, failures));
    }
}
