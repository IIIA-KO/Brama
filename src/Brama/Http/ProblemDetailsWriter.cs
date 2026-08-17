using Brama.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;

namespace Brama.Http;

/// <summary>
/// The single path from a Brama <see cref="Error"/> to an RFC 9457 problem+json response.
/// </summary>
/// <remarks>
/// Handler-returned errors, unhandled exceptions, and the 401/403 raised by ASP.NET Core's own
/// authorization middleware all go through this one writer, so a client sees one response shape
/// regardless of where in the pipeline the failure came from.
/// </remarks>
internal sealed class ProblemDetailsWriter
{
    private readonly ProblemDetailsConfig _config;
    private readonly IHostEnvironment _environment;
    private readonly IStringLocalizerFactory? _localizerFactory;

    public ProblemDetailsWriter(
        ApiConfig apiConfig,
        IHostEnvironment environment,
        IStringLocalizerFactory? localizerFactory = null)
    {
        _config = apiConfig.ProblemDetailsConfig;
        _environment = environment;
        _localizerFactory = localizerFactory;
    }

    public IResult Write(Error error, HttpContext httpContext)
    {
        int statusCode = StatusCodeFor(error.Kind);
        Dictionary<string, object?>? extensions = error.Failures is { Count: > 0 } failures
            ? new Dictionary<string, object?>(StringComparer.Ordinal) { ["errors"] = failures }
            : null;

        return Results.Problem(
            detail: Localize(error.Kind, error.Detail),
            instance: httpContext.Request.Path,
            statusCode: statusCode,
            title: TitleFor(error.Kind),
            extensions: extensions);
    }

    /// <summary>
    /// Renders a failure raised by middleware rather than a handler — notably the empty-bodied
    /// 401/403 ASP.NET Core would otherwise produce.
    /// </summary>
    public IResult WriteKind(ErrorKind kind, HttpContext httpContext) =>
        Write(new Error(kind, DefaultDetailFor(kind)), httpContext);

    /// <summary>
    /// Renders an unhandled exception. The <c>detail</c> string comes from the host's
    /// <c>OnUnhandledException</c> callback, if it registered one — Brama never puts exception text
    /// in a response on its own initiative, in any environment.
    /// </summary>
    public IResult WriteException(Exception exception, HttpContext httpContext)
    {
        var errorContext = new ExceptionErrorContext(httpContext, _environment.IsDevelopment());
        string detail = _config.UnhandledExceptionDetail?.Invoke(exception, errorContext)
            ?? "An unexpected error occurred.";

        return Results.Problem(
            detail: detail,
            instance: httpContext.Request.Path,
            statusCode: StatusCodes.Status500InternalServerError,
            title: "An unexpected error occurred");
    }

    private int StatusCodeFor(ErrorKind kind) =>
        _config.StatusCodes.TryGetValue(kind, out int statusCode)
            ? statusCode
            : throw new InvalidOperationException(
                $"No HTTP status code is mapped for ErrorKind.{kind}. Add .Map(ErrorKind.{kind}, …) " +
                "to the ProblemDetails configuration. This is normally caught at build time as BRAMA001.");

    private string Localize(ErrorKind kind, string detail)
    {
        if (!_config.LocalizationEnabled || _localizerFactory is null)
        {
            return detail;
        }

        IStringLocalizer localizer = _localizerFactory.Create("ProblemDetails", typeof(ProblemDetailsWriter).Assembly.GetName().Name!);
        LocalizedString localized = localizer[kind.ToString()];

        return localized.ResourceNotFound ? detail : localized.Value;
    }

    private static string TitleFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => "Validation failed",
        ErrorKind.NotFound => "Resource not found",
        ErrorKind.Conflict => "Conflict",
        ErrorKind.Unauthorized => "Unauthorized",
        ErrorKind.Forbidden => "Forbidden",
        ErrorKind.Problem => "Request could not be processed",
        _ => "Request could not be processed",
    };

    private static string DefaultDetailFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Unauthorized => "Authentication is required.",
        ErrorKind.Forbidden => "You are not permitted to perform this operation.",
        _ => "The request could not be processed.",
    };

    private sealed record ExceptionErrorContext(HttpContext HttpContext, bool IsDevelopment) : IErrorContext;
}
