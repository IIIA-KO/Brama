namespace Brama;

/// <summary>
/// A failure returned by a handler. Carries the category and a human-readable detail; the HTTP
/// status code is decided later, by the one mapping table configured on <c>ApiConfig</c>.
/// </summary>
/// <param name="Kind">Which category of failure this is.</param>
/// <param name="Detail">The RFC 9457 <c>detail</c> string.</param>
/// <param name="Failures">Field-level problems, when <paramref name="Kind"/> is a validation failure.</param>
public sealed record Error(ErrorKind Kind, string Detail, IReadOnlyList<ValidationFailure>? Failures = null);

/// <summary>
/// An error that has not yet been attached to a response type.
/// </summary>
/// <remarks>
/// This exists so <c>return ctx.NotFound();</c> compiles on its own. A failure carries no value, so
/// there is nothing for C# to infer a <c>Result&lt;T&gt;</c> from; this type converts implicitly
/// into whichever <c>Result&lt;T&gt;</c> the surrounding handler returns.
/// </remarks>
/// <param name="Error">The failure being returned.</param>
public readonly record struct ErrorResult(Error Error);
