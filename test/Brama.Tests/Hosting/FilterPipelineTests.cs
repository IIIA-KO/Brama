using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Brama.Configuration;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Brama.Tests.Hosting;

public class FilterPipelineTests
{
    [Fact]
    public async Task Filters_run_root_first_wrapping_the_handler()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            api.UseFilter<RootFilter>();
            api.Group("Widgets", g => g.UseFilter<GroupFilter>())
                .MapGet<string>("/", (IEndpointContext ctx) =>
                {
                    CallLog.Record("handler");
                    return ctx.Ok("done");
                });
        });

        CallLog.Reset();
        await host.Client.GetAsync(new Uri("/widgets", UriKind.Relative));

        Assert.Equal(
            ["root:in", "group:in", "handler", "group:out", "root:out"],
            CallLog.Entries);
    }

    [Fact]
    public async Task A_filter_can_short_circuit_with_a_result_and_it_renders_as_problem_json()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            api.Group("Widgets", g => g.UseFilter<ShortCircuitingFilter>())
                .MapGet<string>("/", (IEndpointContext ctx) => ctx.Ok("never reached"));
        });

        HttpResponseMessage response = await host.Client.GetAsync(new Uri("/widgets", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Blocked by a filter.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Replace_drops_inherited_filters_for_one_subtree()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            api.UseFilter<ShortCircuitingFilter>();
            api.Group("Widgets", g => g.Replace(c => c.Filters))
                .MapGet<string>("/", (IEndpointContext ctx) => ctx.Ok("reached"));
        });

        Assert.Equal("reached", await host.Client.GetFromJsonAsync<string>(new Uri("/widgets", UriKind.Relative)));
    }

    [Fact]
    public async Task An_exception_from_a_handler_becomes_problem_json_via_the_configured_callback()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            api.ProblemDetails(p => p.OnUnhandledException((exception, context) =>
                context.IsDevelopment ? exception.Message : "An unexpected error occurred"));

            api.Group("Widgets").MapGet<string>("/", Result<string> (IEndpointContext _) =>
                throw new InvalidOperationException("the database is on fire"));
        });

        HttpResponseMessage response = await host.Client.GetAsync(new Uri("/widgets", UriKind.Relative));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("the database is on fire", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task An_exception_reveals_nothing_when_no_callback_is_configured()
    {
        await using BramaTestHost host = await BramaTestHost.StartAsync(api =>
        {
            BramaTestHost.MapAllErrorKinds(api);
            api.Group("Widgets").MapGet<string>("/", Result<string> (IEndpointContext _) =>
                throw new InvalidOperationException("connection string: super-secret"));
        });

        HttpResponseMessage response = await host.Client.GetAsync(new Uri("/widgets", UriKind.Relative));
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("super-secret", body, StringComparison.Ordinal);
    }

    private static class CallLog
    {
        private static readonly List<string> Log = [];

        public static IReadOnlyList<string> Entries
        {
            get
            {
                lock (Log)
                {
                    return [.. Log];
                }
            }
        }

        public static void Record(string entry)
        {
            lock (Log)
            {
                Log.Add(entry);
            }
        }

        public static void Reset()
        {
            lock (Log)
            {
                Log.Clear();
            }
        }
    }

    private sealed class RootFilter : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            CallLog.Record("root:in");
            object? result = await next(context);
            CallLog.Record("root:out");

            return result;
        }
    }

    private sealed class GroupFilter : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            CallLog.Record("group:in");
            object? result = await next(context);
            CallLog.Record("group:out");

            return result;
        }
    }

    private sealed class ShortCircuitingFilter : IEndpointFilter
    {
        public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
            ValueTask.FromResult<object?>(Result.Failure(ErrorKind.Forbidden, "Blocked by a filter."));
    }
}
