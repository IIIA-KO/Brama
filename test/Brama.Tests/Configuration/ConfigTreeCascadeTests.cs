using Brama.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Brama.Tests.Configuration;

public class ConfigTreeCascadeTests
{
    [Fact]
    public void List_fields_append_down_the_chain()
    {
        var api = new ApiConfig();
        api.RequireAuthorization("Root");

        EndpointGroup orders = api.Group("Orders", g => g.RequireAuthorization("OrdersRead"));
        EndpointGroup admin = orders.Group("Admin", g => g.RequireAuthorization("OrdersWrite"));

        Assert.Equal(["Root", "OrdersRead", "OrdersWrite"], admin.Resolve().AuthorizationPolicies);
    }

    [Fact]
    public void List_fields_preserve_root_first_ordering()
    {
        var api = new ApiConfig();
        api.UseFilter<RootFilter>();

        EndpointGroup orders = api.Group("Orders", g => g.UseFilter<GroupFilter>());

        Assert.Equal([typeof(RootFilter), typeof(GroupFilter)], orders.Resolve().Filters);
    }

    [Fact]
    public void Scalar_fields_replace_rather_than_accumulate()
    {
        var api = new ApiConfig();
        api.Version(1);

        EndpointGroup v2 = api.Group("OrdersV2", g => g.Version(2));

        Assert.Equal(2, v2.Resolve().Version);
    }

    [Fact]
    public void Scalar_fields_inherit_when_the_child_does_not_set_them()
    {
        var api = new ApiConfig();
        api.Version(3).LogLevel(LogLevel.Debug);

        EndpointGroup orders = api.Group("Orders");

        ResolvedConfig resolved = orders.Resolve();
        Assert.Equal(3, resolved.Version);
        Assert.Equal(LogLevel.Debug, resolved.LogLevel);
    }

    [Fact]
    public void Replace_drops_an_inherited_list_for_one_subtree_only()
    {
        var api = new ApiConfig();
        api.UseFilter<RootFilter>();

        EndpointGroup orders = api.Group("Orders", g => g.UseFilter<GroupFilter>());
        EndpointGroup admin = orders.Group("Admin", g => g.Replace(c => c.Filters));
        EndpointGroup reports = orders.Group("Reports");

        Assert.Empty(admin.Resolve().Filters);
        Assert.Equal([typeof(RootFilter), typeof(GroupFilter)], reports.Resolve().Filters);
    }

    [Fact]
    public void Replace_clears_before_the_declaring_node_applies_its_own_value()
    {
        var api = new ApiConfig();
        api.UseFilter<RootFilter>();

        EndpointGroup orders = api.Group("Orders", g => g
            .Replace(c => c.Filters)
            .UseFilter<GroupFilter>());

        Assert.Equal([typeof(GroupFilter)], orders.Resolve().Filters);
    }

    [Fact]
    public void Replace_clears_an_inherited_scalar()
    {
        var api = new ApiConfig();
        api.RequireRateLimiting("global");

        EndpointGroup unlimited = api.Group("Health", g => g.Replace(c => c.RateLimitPolicy));

        Assert.Null(unlimited.Resolve().RateLimitPolicy);
    }

    [Fact]
    public void Replace_clears_an_inherited_flag()
    {
        var api = new ApiConfig();
        api.LogRequests();

        EndpointGroup quiet = api.Group("Health", g => g.Replace(c => c.LogRequests));

        Assert.False(quiet.Resolve().LogRequests);
    }

    [Fact]
    public void Replace_rejects_an_expression_that_is_not_a_property_access()
    {
        var api = new ApiConfig();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => api.Replace(c => c.Filters.Count > 0));

        Assert.Equal("field", exception.ParamName);
    }

    [Fact]
    public void Unset_fields_resolve_to_their_inherit_nothing_defaults()
    {
        ResolvedConfig resolved = new ApiConfig().Resolve();

        Assert.Empty(resolved.Filters);
        Assert.Empty(resolved.AuthorizationPolicies);
        Assert.Null(resolved.Version);
        Assert.Null(resolved.Sunset);
        Assert.False(resolved.Deprecated);
        Assert.False(resolved.LogRequests);
    }

    [Fact]
    public void Groups_contribute_their_lower_cased_name_as_a_route_segment()
    {
        var api = new ApiConfig();
        EndpointGroup admin = api.Group("Orders").Group("Admin");

        Assert.Equal(["orders", "admin"], admin.Resolve().RouteSegments);
    }

    [Fact]
    public void BaseRoute_overrides_the_segment_derived_from_the_group_name()
    {
        var api = new ApiConfig();
        EndpointGroup v2 = api.Group("OrdersV2", g => g.BaseRoute("orders").Version(2));

        Assert.Equal(["orders"], v2.Resolve().RouteSegments);
    }

    [Fact]
    public void Sibling_groups_do_not_leak_settings_into_each_other()
    {
        var api = new ApiConfig();
        EndpointGroup orders = api.Group("Orders", g => g.RequireAuthorization("OrdersRead"));
        EndpointGroup payments = api.Group("Payments", g => g.RequireAuthorization("PaymentsRead"));

        Assert.Equal(["OrdersRead"], orders.Resolve().AuthorizationPolicies);
        Assert.Equal(["PaymentsRead"], payments.Resolve().AuthorizationPolicies);
    }

    [Fact]
    public void Children_are_tracked_so_the_whole_tree_can_be_walked()
    {
        var api = new ApiConfig();
        EndpointGroup orders = api.Group("Orders");
        orders.Group("Admin");

        Assert.Equal([orders], api.Children);
        Assert.Single(orders.Children);
        Assert.Equal("Admin", orders.Children[0].Name);
    }

    [Fact]
    public void Inline_rate_limit_and_headers_resolve_together()
    {
        var api = new ApiConfig();
        EndpointGroup writes = api.Group("Writes", g => g
            .RateLimit(permitLimit: 100, window: TimeSpan.FromMinutes(1))
            .EmitRateLimitHeaders());

        ResolvedConfig resolved = writes.Resolve();

        Assert.Equal(new RateLimitPolicySpec(100, TimeSpan.FromMinutes(1)), resolved.InlineRateLimit);
        Assert.True(resolved.EmitRateLimitHeaders);
    }

    [Fact]
    public void Deprecation_metadata_cascades_to_every_endpoint_of_a_version()
    {
        var api = new ApiConfig();
        EndpointGroup v1 = api.Group("OrdersV1", g => g
            .Version(1)
            .Deprecated()
            .Sunset(new DateOnly(2027, 1, 1)));

        EndpointGroup nested = v1.Group("Admin");
        ResolvedConfig resolved = nested.Resolve();

        Assert.True(resolved.Deprecated);
        Assert.Equal(new DateOnly(2027, 1, 1), resolved.Sunset);
        Assert.Equal(1, resolved.Version);
    }

    [Fact]
    public void Group_rejects_a_blank_name()
    {
        var api = new ApiConfig();

        Assert.Throws<ArgumentException>(() => api.Group("  "));
    }

    private sealed class RootFilter : IEndpointFilter
    {
        public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
            next(context);
    }

    private sealed class GroupFilter : IEndpointFilter
    {
        public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
            next(context);
    }
}
