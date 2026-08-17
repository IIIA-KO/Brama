namespace Brama.Configuration;

/// <summary>
/// The single mapping table from <see cref="ErrorKind"/> to HTTP status code, plus the policy for
/// what an unhandled exception is allowed to reveal.
/// </summary>
/// <remarks>
/// This table is the only place a Brama error becomes a status code — handler-returned errors and
/// the 401/403 raised by ASP.NET Core's own authorization middleware both go through it, so a
/// response body has one shape everywhere.
/// </remarks>
public sealed class ProblemDetailsConfig
{
    private readonly Dictionary<ErrorKind, int> _statusCodes = [];

    internal ProblemDetailsConfig()
    {
    }

    /// <summary>The status code mapped to each <see cref="ErrorKind"/> so far.</summary>
    public IReadOnlyDictionary<ErrorKind, int> StatusCodes => _statusCodes;

    /// <summary>
    /// Whether problem <c>detail</c> strings are resolved through <c>IStringLocalizer</c> and the
    /// request's <c>Accept-Language</c>, falling back to the literal string when untranslated.
    /// </summary>
    public bool LocalizationEnabled { get; private set; }

    /// <summary>
    /// Decides the <c>detail</c> string for an unhandled exception. Without this, Brama never puts
    /// exception text in a response.
    /// </summary>
    public Func<Exception, IErrorContext, string>? UnhandledExceptionDetail { get; private set; }

    /// <summary>Maps one error kind to the HTTP status code it is reported as.</summary>
    public ProblemDetailsConfig Map(ErrorKind kind, int statusCode)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(statusCode, 100);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(statusCode, 599);

        _statusCodes[kind] = statusCode;

        return this;
    }

    /// <summary>
    /// Supplies the <c>detail</c> string for unhandled exceptions. The callback receives the
    /// exception and an <see cref="IErrorContext"/>, so exposing <c>ex.Message</c> outside
    /// Development is an explicit choice the host makes, never a default.
    /// </summary>
    public ProblemDetailsConfig OnUnhandledException(Func<Exception, IErrorContext, string> detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        UnhandledExceptionDetail = detail;

        return this;
    }

    /// <summary>Resolves problem <c>detail</c> strings through the host's <c>IStringLocalizer</c>.</summary>
    public ProblemDetailsConfig UseLocalization()
    {
        LocalizationEnabled = true;

        return this;
    }

    /// <summary>
    /// The error kinds with no status code mapped. Empty is the only valid state at runtime; the
    /// source generator reports anything else as <c>BRAMA001</c> at build time.
    /// </summary>
    internal IReadOnlyList<ErrorKind> UnmappedKinds() =>
        [.. Enum.GetValues<ErrorKind>().Where(kind => !_statusCodes.ContainsKey(kind))];
}
