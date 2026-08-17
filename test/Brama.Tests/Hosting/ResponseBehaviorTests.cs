using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Brama.Tests.Hosting;

public class ResponseBehaviorTests
{
    [Fact]
    public async Task A_deprecated_group_announces_itself_and_its_sunset_date()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            api.Group("OrdersV1", g => g
                    .Version(1)
                    .Deprecated()
                    .Sunset(new DateOnly(2027, 1, 1)))
                .MapGet<string>("/", (IEndpointContext ctx) => ctx.Ok("v1"));
        });

        HttpResponseMessage response = await host.Client.GetAsync(new Uri("/ordersv1", UriKind.Relative));

        Assert.Equal("true", response.Headers.GetValues("Deprecation").Single());
        Assert.Equal("Fri, 01 Jan 2027 00:00:00 GMT", response.Headers.GetValues("Sunset").Single());
    }

    [Fact]
    public async Task Endpoints_that_opt_out_carry_no_deprecation_headers()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            api.Group("Orders").MapGet<string>("/", (IEndpointContext ctx) => ctx.Ok("current"));
        });

        HttpResponseMessage response = await host.Client.GetAsync(new Uri("/orders", UriKind.Relative));

        Assert.False(response.Headers.Contains("Deprecation"));
        Assert.False(response.Headers.Contains("Sunset"));
    }

    [Fact]
    public async Task Rate_limit_headers_report_the_inline_policys_own_limit()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            api.Group("Writes", g => g
                    .RateLimit(permitLimit: 100, window: TimeSpan.FromMinutes(1))
                    .EmitRateLimitHeaders())
                .MapGet<string>("/", (IEndpointContext ctx) => ctx.Ok("ok"));
        });

        HttpResponseMessage response = await host.Client.GetAsync(new Uri("/writes", UriKind.Relative));

        Assert.Equal("100", response.Headers.GetValues("RateLimit-Limit").Single());
        Assert.Equal("60", response.Headers.GetValues("RateLimit-Reset").Single());
    }

    [Fact]
    public async Task A_correlation_id_is_echoed_back_when_the_caller_supplies_one()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            api.Group("Orders", g => g.PropagateCorrelationId())
                .MapGet<string>("/", (IEndpointContext ctx) => ctx.Ok("ok"));
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/orders", UriKind.Relative));
        request.Headers.Add("X-Correlation-Id", "abc-123");

        HttpResponseMessage response = await host.Client.SendAsync(request);

        Assert.Equal("abc-123", response.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task A_correlation_id_is_minted_when_the_caller_supplies_none()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            api.Group("Orders", g => g.PropagateCorrelationId())
                .MapGet<string>("/", (IEndpointContext ctx) => ctx.Ok("ok"));
        });

        HttpResponseMessage response = await host.Client.GetAsync(new Uri("/orders", UriKind.Relative));

        Assert.NotEmpty(response.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task A_correlation_id_is_echoed_on_failures_too()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            api.Group("Orders", g => g.PropagateCorrelationId())
                .MapGet<string>("/", (IEndpointContext ctx) => ctx.NotFound());
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/orders", UriKind.Relative));
        request.Headers.Add("X-Correlation-Id", "trace-me");

        HttpResponseMessage response = await host.Client.SendAsync(request);

        Assert.Equal("trace-me", response.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task Request_logging_records_the_status_the_client_received()
    {
        using var log = new RecordingLoggerProvider();

        await using BramaTestHost host = await BramaTestHost.StartAsync(
            api =>
            {
                BramaTestHost.MapAllErrorKinds(api);
                api.Group("Orders", g => g.LogRequests())
                    .MapGet<string>("/", (IEndpointContext ctx) => ctx.NotFound());
            },
            services => services.AddLogging(logging => logging.AddProvider(log).SetMinimumLevel(LogLevel.Trace)));

        // Drained, because the log entry is written when the response completes — not when the
        // client has merely received its headers.
        using HttpResponseMessage response = await host.Client.GetAsync(new Uri("/orders", UriKind.Relative));
        await response.Content.ReadAsStringAsync();

        string entry = Assert.Single(log.Entries);
        Assert.Contains("404", entry, StringComparison.Ordinal);
        Assert.Contains("/orders", entry, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_is_logged_for_a_group_that_did_not_ask_for_it()
    {
        using var log = new RecordingLoggerProvider();

        await using BramaTestHost host = await BramaTestHost.StartAsync(
            api =>
            {
                BramaTestHost.MapAllErrorKinds(api);
                api.Group("Orders").MapGet<string>("/", (IEndpointContext ctx) => ctx.Ok("ok"));
            },
            services => services.AddLogging(logging => logging.AddProvider(log).SetMinimumLevel(LogLevel.Trace)));

        using HttpResponseMessage response = await host.Client.GetAsync(new Uri("/orders", UriKind.Relative));
        await response.Content.ReadAsStringAsync();

        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task The_configured_log_level_is_the_one_used()
    {
        using var log = new RecordingLoggerProvider();

        await using BramaTestHost host = await BramaTestHost.StartAsync(
            api =>
            {
                BramaTestHost.MapAllErrorKinds(api);
                api.Group("Orders", g => g.LogRequests().LogLevel(LogLevel.Warning))
                    .MapGet<string>("/", (IEndpointContext ctx) => ctx.Ok("ok"));
            },
            services => services.AddLogging(logging => logging.AddProvider(log).SetMinimumLevel(LogLevel.Trace)));

        using HttpResponseMessage response = await host.Client.GetAsync(new Uri("/orders", UriKind.Relative));
        await response.Content.ReadAsStringAsync();

        Assert.Equal(LogLevel.Warning, Assert.Single(log.Levels));
    }
}
