using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;

namespace Brama.Http;

/// <summary>
/// Turns the <see cref="Result{T}"/> a handler returned into an HTTP response, so no endpoint ever
/// writes <c>result.Match(Results.Ok, Problem)</c> itself.
/// </summary>
/// <remarks>
/// Registered as the innermost endpoint filter, which is what lets a user filter short-circuit by
/// returning a <see cref="Result{T}"/> of its own and have it rendered through this same path.
/// </remarks>
/// <typeparam name="T">The endpoint's response body type.</typeparam>
internal sealed class ResultConversionFilter<T> : IEndpointFilter
{
    private readonly ProblemDetailsWriter _problemDetails;

    public ResultConversionFilter(ProblemDetailsWriter problemDetails) => _problemDetails = problemDetails;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        object? returned = await next(context);

        return returned switch
        {
            Result<T> result => Convert(result, context.HttpContext),
            ErrorResult error => _problemDetails.Write(error.Error, context.HttpContext),
            _ => returned,
        };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2234:Pass system uri objects instead of strings",
        Justification = "A Location header is a relative path here; wrapping it in a Uri would force an absolute URI.")]
    private IResult Convert(Result<T> result, HttpContext httpContext)
    {
        if (!result.IsSuccess)
        {
            return _problemDetails.Write(result.Error!, httpContext);
        }

        if (result.SuccessStatusCode == StatusCodes.Status204NoContent)
        {
            return Results.NoContent();
        }

        object? body = result.Links.IsDefaultOrEmpty
            ? result.Value
            : WithLinks(result, httpContext);

        return result.SuccessStatusCode == StatusCodes.Status201Created
            ? Results.Created(result.Location, body)
            : Results.Json(body, statusCode: result.SuccessStatusCode);
    }

    /// <summary>
    /// Adds the <c>_links</c> extension member to the serialized body. Only reached when a handler
    /// actually called <c>WithLink</c>; otherwise the value is serialized untouched, so enabling
    /// hypermedia cannot change the shape of a response that does not use it.
    /// </summary>
    private static JsonNode? WithLinks(Result<T> result, HttpContext httpContext)
    {
        JsonSerializerOptions options = httpContext.RequestServices
            .GetService(typeof(Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>))
            is Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions
            ? jsonOptions.Value.SerializerOptions
            : JsonSerializerOptions.Web;

        JsonNode? node = JsonSerializer.SerializeToNode(result.Value, options);
        if (node is not JsonObject envelope)
        {
            return node;
        }

        var links = new JsonObject();
        foreach (Link link in result.Links)
        {
            links[link.Rel] = new JsonObject { ["href"] = link.Href };
        }

        envelope["_links"] = links;

        return envelope;
    }
}
