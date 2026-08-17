using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Brama.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Brama.Tests.Hosting;

public record OrderView(Guid Id);

/// <summary>
/// The config-tree settings that are handed to ASP.NET Core rather than implemented by Brama.
/// Brama's job for each is to attach the right metadata to the right endpoints, and to turn on the
/// middleware that reads it; these check it actually does.
/// </summary>
public class EndpointSettingsTests
{
    [Fact]
    public async Task A_named_endpoint_can_be_linked_to_and_the_path_includes_the_version_prefix()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            api.Versioning(v => v.UseUrlSegment());

            EndpointGroup orders = api.Group("Orders", g => g.Version(1));

            orders.MapGet<OrderView>("/{id:guid}", (Guid id, IEndpointContext ctx) => ctx.Ok(new OrderView(id)), e => e.Named("GetOrder"));
            orders.MapGet<OrderView>("/", (IEndpointContext ctx) =>
                ctx.Ok(new OrderView(Guid.Empty))
                    .WithLink("first", ctx.PathTo("GetOrder", new { id = Guid.Empty })));
        });

        JsonElement body = await host.Client.GetFromJsonAsync<JsonElement>(new Uri("/v1/orders", UriKind.Relative));

        Assert.Equal(
            $"/v1/orders/{Guid.Empty}",
            body.GetProperty("_links").GetProperty("first").GetProperty("href").GetString());
    }

    [Fact]
    public async Task DisallowLinks_strips_links_from_a_subtree_that_forbade_them()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            api.Group("Orders", g => g.DisallowLinks())
                .MapGet<OrderView>("/", (IEndpointContext ctx) =>
                    ctx.Ok(new OrderView(Guid.Empty)).WithLink("self", "/orders"));
        });

        JsonElement body = await host.Client.GetFromJsonAsync<JsonElement>(new Uri("/orders", UriKind.Relative));

        Assert.False(body.TryGetProperty("_links", out _));
    }

    /// <summary>
    /// A scalar body has no member to hang <c>_links</c> off. Brama drops them and says so, rather
    /// than inventing an envelope and changing the response shape out from under the client.
    /// </summary>
    [Fact]
    public async Task Links_on_a_scalar_response_body_are_dropped_with_a_warning()
    {
        using var log = new RecordingLoggerProvider();

        await using BramaTestHost host = await BramaTestHost.StartAsync(
            api =>
            {
                BramaTestHost.MapAllErrorKinds(api);
                api.Group("Orders").MapGet<string>("/", (IEndpointContext ctx) =>
                    ctx.Ok("plain").WithLink("self", "/orders"));
            },
            services => services.AddLogging(logging => logging.AddProvider(log)));

        Assert.Equal("plain", await host.Client.GetFromJsonAsync<string>(new Uri("/orders", UriKind.Relative)));
        Assert.Contains(log.Entries, entry => entry.Contains("nowhere to put", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_authorization_policy_on_a_group_is_enforced_and_denied_as_problem_json()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(
            api =>
            {
                BramaTestHost.MapAllErrorKinds(api);
                api.Group("Orders", g => g.RequireAuthorization("NobodyPasses"))
                    .MapGet<string>("/", (IEndpointContext ctx) => ctx.Ok("secret"));
            },
            services => services.AddAuthorizationBuilder()
                .AddPolicy("NobodyPasses", policy => policy.RequireAssertion(_ => false)));

        HttpResponseMessage response = await host.Client.GetAsync(new Uri("/orders", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AllowAnonymous_on_a_subtree_overrides_an_inherited_policy()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(
            api =>
            {
                BramaTestHost.MapAllErrorKinds(api);
                api.RequireAuthorization("NobodyPasses");
                api.Group("Health", g => g.AllowAnonymous())
                    .MapGet<string>("/", (IEndpointContext ctx) => ctx.Ok("healthy"));
            },
            services => services.AddAuthorizationBuilder()
                .AddPolicy("NobodyPasses", policy => policy.RequireAssertion(_ => false)));

        Assert.Equal("healthy", await host.Client.GetFromJsonAsync<string>(new Uri("/health", UriKind.Relative)));
    }

    [Fact]
    public async Task An_inline_rate_limit_rejects_once_the_permit_is_spent()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            api.Group("Writes", g => g.RateLimit(permitLimit: 1, window: TimeSpan.FromMinutes(5)))
                .MapGet<string>("/", (IEndpointContext ctx) => ctx.Ok("ok"));
        });

        var uri = new Uri("/writes", UriKind.Relative);

        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(uri)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Client.GetAsync(uri)).StatusCode);
    }

    [Fact]
    public async Task Output_caching_serves_the_second_request_without_running_the_handler()
    {
        int handlerRuns = 0;

        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            api.Group("Reports", g => g.OutputCache(TimeSpan.FromMinutes(5)))
                .MapGet<int>("/", (IEndpointContext ctx) => ctx.Ok(++handlerRuns));
        });

        var uri = new Uri("/reports", UriKind.Relative);

        Assert.Equal(1, await host.Client.GetFromJsonAsync<int>(uri));
        Assert.Equal(1, await host.Client.GetFromJsonAsync<int>(uri));
        Assert.Equal(1, handlerRuns);
    }

    /// <summary>
    /// A host that already added the CORS middleware itself must not be broken by Brama adding it
    /// again — Brama's guard is "the tree asked for it", not "nobody else did".
    /// </summary>
    [Fact]
    public async Task Cors_still_works_when_the_host_already_added_the_middleware()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(
            api =>
            {
                BramaTestHost.MapAllErrorKinds(api);
                api.Group("Orders", g => g.Cors("trusted"))
                    .MapGet<string>("/", (IEndpointContext ctx) => ctx.Ok("ok"));
            },
            services => services.AddCors(options => options.AddPolicy(
                "trusted",
                policy => policy.WithOrigins("https://trusted.example").AllowAnyHeader())),
            app => app.UseCors());

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/orders", UriKind.Relative));
        request.Headers.Add("Origin", "https://trusted.example");

        HttpResponseMessage response = await host.Client.SendAsync(request);

        Assert.Equal("ok", await response.Content.ReadFromJsonAsync<string>());
        Assert.Equal(
            "https://trusted.example",
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task A_named_cors_policy_is_applied_to_the_endpoints_that_asked_for_it()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(
            api =>
            {
                BramaTestHost.MapAllErrorKinds(api);
                api.Group("Orders", g => g.Cors("trusted"))
                    .MapGet<string>("/", (IEndpointContext ctx) => ctx.Ok("ok"));
            },
            services => services.AddCors(options => options.AddPolicy(
                "trusted",
                policy => policy.WithOrigins("https://trusted.example").AllowAnyHeader())));

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/orders", UriKind.Relative));
        request.Headers.Add("Origin", "https://trusted.example");

        HttpResponseMessage response = await host.Client.SendAsync(request);

        Assert.Equal(
            "https://trusted.example",
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }
}
