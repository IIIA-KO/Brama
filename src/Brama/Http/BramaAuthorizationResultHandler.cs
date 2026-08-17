using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Brama.Http;

/// <summary>
/// Routes the 401/403 raised by ASP.NET Core's authorization middleware through Brama's own
/// problem-details table.
/// </summary>
/// <remarks>
/// Without this, an authorization failure produces an empty-bodied response while a handler-returned
/// <see cref="ErrorKind.Forbidden"/> produces problem+json — two error shapes from one API, which
/// is exactly the inconsistency the design set out to remove.
/// </remarks>
internal sealed class BramaAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private static readonly AuthorizationMiddlewareResultHandler Default = new();

    private readonly ProblemDetailsWriter _problemDetails;

    public BramaAuthorizationResultHandler(ProblemDetailsWriter problemDetails) => _problemDetails = problemDetails;

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult.Challenged)
        {
            await WriteAsync(ErrorKind.Unauthorized, context);
            return;
        }

        if (authorizeResult.Forbidden)
        {
            await WriteAsync(ErrorKind.Forbidden, context);
            return;
        }

        await Default.HandleAsync(next, context, policy, authorizeResult);
    }

    private Task WriteAsync(ErrorKind kind, HttpContext context) =>
        _problemDetails.WriteKind(kind, context).ExecuteAsync(context);
}
