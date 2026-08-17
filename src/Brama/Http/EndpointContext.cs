using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Brama.Http;

/// <summary>
/// The request-scoped <see cref="IEndpointContext"/> handed to handlers. Bound as an ordinary
/// parameter, because <c>AddBrama</c> registers it in DI and minimal APIs bind registered services
/// rather than treating them as request bodies.
/// </summary>
internal sealed class EndpointContext : IEndpointContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IHostEnvironment _environment;

    public EndpointContext(IHttpContextAccessor httpContextAccessor, IHostEnvironment environment)
    {
        _httpContextAccessor = httpContextAccessor;
        _environment = environment;
    }

    public HttpContext HttpContext =>
        _httpContextAccessor.HttpContext
        ?? throw new InvalidOperationException("IEndpointContext was used outside of a request.");

    public bool IsDevelopment => _environment.IsDevelopment();

    public string PathTo(string routeName, object? routeValues = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeName);

        return Get<LinkGenerator>().GetPathByName(HttpContext, routeName, routeValues)
            ?? throw new InvalidOperationException(
                $"No endpoint named '{routeName}' could be found. Names come from Named(\"...\") on an endpoint.");
    }

    public TService Get<TService>()
        where TService : notnull =>
        HttpContext.RequestServices.GetRequiredService<TService>();

    public Result<T> Ok<T>(T value) => Result.Ok(value);

    public Result<T> Created<T>(T value, object id)
    {
        ArgumentNullException.ThrowIfNull(id);

        string path = HttpContext.Request.Path.Value?.TrimEnd('/') ?? string.Empty;
        string identifier = System.Convert.ToString(id, CultureInfo.InvariantCulture) ?? string.Empty;

        return Result.Created(value, $"{path}/{identifier}");
    }

    public Result<T> CreatedAt<T>(T value, string location) => Result.Created(value, location);

    public Result<T> NoContent<T>() => Result.NoContent<T>();

    public ErrorResult NotFound(string? detail = null) =>
        Result.Failure(ErrorKind.NotFound, detail ?? "The requested resource was not found.");

    public ErrorResult Conflict(string? detail = null) =>
        Result.Failure(ErrorKind.Conflict, detail ?? "The request conflicts with the current state of the resource.");

    public ErrorResult Forbidden(string? detail = null) =>
        Result.Failure(ErrorKind.Forbidden, detail ?? "You are not permitted to perform this operation.");

    public ErrorResult Unauthorized(string? detail = null) =>
        Result.Failure(ErrorKind.Unauthorized, detail ?? "Authentication is required.");

    public ErrorResult Error(ErrorKind kind, string detail) => Result.Failure(kind, detail);

    public ErrorResult Invalid(string detail, IReadOnlyList<ValidationFailure> failures) =>
        Result.Invalid(detail, failures);
}
