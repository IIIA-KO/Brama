namespace Brama;

/// <summary>
/// One field-level validation problem, rendered into the <c>errors</c> extension member of an
/// RFC 9457 problem+json response.
/// </summary>
/// <param name="Field">The request field that failed, in the shape the client sent it.</param>
/// <param name="Message">What is wrong with it.</param>
public sealed record ValidationFailure(string Field, string Message);
