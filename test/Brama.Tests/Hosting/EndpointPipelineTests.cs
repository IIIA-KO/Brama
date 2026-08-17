using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Brama.Configuration;
using Brama.Tests.Hosting;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Brama.Tests.Hosting;

public class EndpointPipelineTests
{
    private sealed record Widget(Guid Id, string Name);

    [Fact]
    public async Task Success_is_serialized_with_the_declared_status_code()
    {
        var id = Guid.NewGuid();
        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            api.Group("Widgets").MapGet<Widget>("/{id:guid}", (Guid id, IEndpointContext ctx) =>
                ctx.Ok(new Widget(id, "Sprocket")));
        });

        HttpResponseMessage response = await host.Client.GetAsync(new Uri($"/widgets/{id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Widget? widget = await response.Content.ReadFromJsonAsync<Widget>();
        Assert.Equal(new Widget(id, "Sprocket"), widget);
    }

    [Fact]
    public async Task Group_names_become_the_route_prefix()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            api.Group("Widgets").Group("Archived").MapGet<string>("/", (IEndpointContext ctx) => ctx.Ok("archived"));
        });

        HttpResponseMessage response = await host.Client.GetAsync(new Uri("/widgets/archived", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Created_sets_the_location_header_from_the_request_path()
    {
        var id = Guid.NewGuid();
        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            api.Group("Widgets").MapPost<Widget>("/", (IEndpointContext ctx) =>
                ctx.Created(new Widget(id, "Sprocket"), id: id));
        });

        HttpResponseMessage response = await host.Client.PostAsync(new Uri("/widgets", UriKind.Relative), content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/widgets/{id}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task NoContent_returns_204_with_an_empty_body()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            api.Group("Widgets").MapDelete<Widget>("/", (IEndpointContext ctx) => ctx.NoContent<Widget>());
        });

        HttpResponseMessage response = await host.Client.DeleteAsync(new Uri("/widgets", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_error_is_rendered_as_problem_json_with_the_mapped_status_code()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            api.Group("Widgets").MapGet<Widget>("/", (IEndpointContext ctx) => ctx.NotFound("No such widget."));
        });

        HttpResponseMessage response = await host.Client.GetAsync(new Uri("/widgets", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("No such widget.", problem.GetProperty("detail").GetString());
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.Equal("/widgets", problem.GetProperty("instance").GetString());
    }

    [Fact]
    public async Task A_validation_failure_carries_per_field_errors()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            api.Group("Widgets").MapPost<Widget>("/", (IEndpointContext ctx) => ctx.Invalid(
                "The widget could not be created.",
                [new ValidationFailure("Name", "Name is required.")]));
        });

        HttpResponseMessage response = await host.Client.PostAsync(new Uri("/widgets", UriKind.Relative), content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement errors = problem.GetProperty("errors");
        Assert.Equal("Name", errors[0].GetProperty("field").GetString());
        Assert.Equal("Name is required.", errors[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task The_status_code_for_a_kind_comes_from_the_configured_table_not_a_convention()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            api.ProblemDetails(p => p
                .Map(ErrorKind.Validation, StatusCodes.Status400BadRequest)
                .Map(ErrorKind.NotFound, StatusCodes.Status404NotFound)
                .Map(ErrorKind.Conflict, StatusCodes.Status409Conflict)
                .Map(ErrorKind.Unauthorized, StatusCodes.Status401Unauthorized)
                .Map(ErrorKind.Forbidden, StatusCodes.Status403Forbidden)
                .Map(ErrorKind.Problem, StatusCodes.Status418ImATeapot));

            api.Group("Widgets").MapGet<Widget>("/", (IEndpointContext ctx) =>
                ctx.Error(ErrorKind.Problem, "Nope."));
        });

        HttpResponseMessage response = await host.Client.GetAsync(new Uri("/widgets", UriKind.Relative));

        Assert.Equal(StatusCodes.Status418ImATeapot, (int)response.StatusCode);
    }

    [Fact]
    public async Task Startup_fails_when_an_error_kind_has_no_status_code()
    {
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BramaTestHost.StartAsync(api =>
            {
                api.ProblemDetails(p => p.Map(ErrorKind.NotFound, StatusCodes.Status404NotFound));
                api.Group("Widgets").MapGet<Widget>("/", (IEndpointContext ctx) => ctx.NotFound());
            }));

        Assert.Contains("ErrorKind.Conflict", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Links_appear_only_when_a_handler_attaches_them()
    {
        var id = Guid.NewGuid();
        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            EndpointGroup widgets = api.Group("Widgets");

            widgets.MapGet<Widget>("/plain", (IEndpointContext ctx) => ctx.Ok(new Widget(id, "Sprocket")));
            widgets.MapGet<Widget>("/linked", (IEndpointContext ctx) => ctx.Ok(new Widget(id, "Sprocket"))
                .WithLink("self", $"/widgets/{id}")
                .WithLink("cancel", $"/widgets/{id}/cancel", when: false));
        });

        JsonElement plain = await host.Client.GetFromJsonAsync<JsonElement>(new Uri("/widgets/plain", UriKind.Relative));
        Assert.False(plain.TryGetProperty("_links", out _));

        JsonElement linked = await host.Client.GetFromJsonAsync<JsonElement>(new Uri("/widgets/linked", UriKind.Relative));
        JsonElement links = linked.GetProperty("_links");
        Assert.Equal($"/widgets/{id}", links.GetProperty("self").GetProperty("href").GetString());
        Assert.False(links.TryGetProperty("cancel", out _));
    }

    [Fact]
    public async Task Versioning_prefixes_the_route_with_the_version_segment()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            api.Versioning(v => v.UseUrlSegment());

            api.Group("Orders", g => g.Version(1)).MapGet<string>("/", (IEndpointContext ctx) => ctx.Ok("v1"));
            api.Group("OrdersV2", g => g.BaseRoute("orders").Version(2))
                .MapGet<string>("/", (IEndpointContext ctx) => ctx.Ok("v2"));
        });

        Assert.Equal("v1", await host.Client.GetFromJsonAsync<string>(new Uri("/v1/orders", UriKind.Relative)));
        Assert.Equal("v2", await host.Client.GetFromJsonAsync<string>(new Uri("/v2/orders", UriKind.Relative)));
    }

    [Fact]
    public async Task Routes_are_unversioned_when_versioning_is_never_turned_on()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            api.Group("Orders", g => g.Version(1)).MapGet<string>("/", (IEndpointContext ctx) => ctx.Ok("plain"));
        });

        Assert.Equal("plain", await host.Client.GetFromJsonAsync<string>(new Uri("/orders", UriKind.Relative)));
    }
}
