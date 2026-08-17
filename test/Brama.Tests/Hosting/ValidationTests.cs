using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Brama.Tests.Hosting;

public record CreateWidget([property: Required, MaxLength(10)] string Name, int Quantity = 1);

public class ValidationTests
{
    [Fact]
    public async Task A_field_constraint_failure_short_circuits_before_the_handler()
    {
        bool handlerRan = false;
        await using BramaTestHost host = await StartAsync(() => handlerRan = true);

        HttpResponseMessage response = await host.Client.PostAsJsonAsync(
            new Uri("/widgets", UriKind.Relative),
            new CreateWidget(Name: "far too long a name"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(handlerRan);
    }

    [Fact]
    public async Task The_failure_is_problem_json_naming_the_offending_field()
    {
        await using BramaTestHost host = await StartAsync(() => { });

        HttpResponseMessage response = await host.Client.PostAsJsonAsync(
            new Uri("/widgets", UriKind.Relative),
            new CreateWidget(Name: "far too long a name"));

        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Equal("Name", problem.GetProperty("errors")[0].GetProperty("field").GetString());
    }

    [Fact]
    public async Task A_valid_request_reaches_the_handler_untouched()
    {
        bool handlerRan = false;
        await using BramaTestHost host = await StartAsync(() => handlerRan = true);

        HttpResponseMessage response = await host.Client.PostAsJsonAsync(
            new Uri("/widgets", UriKind.Relative),
            new CreateWidget(Name: "Sprocket"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(handlerRan);
    }

    /// <summary>
    /// The failure arrives as a value, never as an exception, so no outer handler can catch it and
    /// re-report it as something else — the specific defect this design set out to remove.
    /// </summary>
    [Fact]
    public async Task A_validation_failure_is_never_reported_as_a_server_error()
    {
        await using BramaTestHost host = await StartAsync(() => { });

        HttpResponseMessage response = await host.Client.PostAsJsonAsync(
            new Uri("/widgets", UriKind.Relative),
            new CreateWidget(Name: null!));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static Task<BramaTestHost> StartAsync(Action onHandlerRun) =>
        BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            api.Group("Widgets").MapPost<string>("/", (CreateWidget body, IEndpointContext ctx) =>
            {
                onHandlerRun();
                return ctx.Ok(body.Name);
            });
        });
}
