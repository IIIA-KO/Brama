using Xunit;

namespace Brama.SourceGenerator.Tests;

/// <summary>
/// Covers the surface model behind Contracts: what the generator can see, and what it declines to
/// guess at. The build-step behaviour that consumes this — freeze, additive update, breaking-change
/// failure — is exercised against the sample application.
/// </summary>
public class ContractSurfaceTests
{
    [Fact]
    public void A_versioned_group_produces_a_contract_block()
    {
        string contract = ContractFor("""
            var orders = api.Group("Orders", g => g.Version(1));
            orders.MapGet<OrderResponse>("/{id:guid}", (System.Guid id, IEndpointContext ctx) => ctx.Ok(new OrderResponse(id, "Placed")));
            """);

        Assert.Contains("BRAMA-CONTRACT-BEGIN Orders.v1", contract, StringComparison.Ordinal);
        Assert.Contains("endpoint  GET /orders/{id:guid}", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void Response_fields_are_recorded_one_fact_per_field()
    {
        string contract = ContractFor("""
            var orders = api.Group("Orders", g => g.Version(1));
            orders.MapGet<OrderResponse>("/{id:guid}", (System.Guid id, IEndpointContext ctx) => ctx.Ok(new OrderResponse(id, "Placed")));
            """);

        Assert.Contains("response.field  GET /orders/{id:guid}  Id : System.Guid", contract, StringComparison.Ordinal);
        Assert.Contains("response.field  GET /orders/{id:guid}  Status : string", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void Request_bodies_are_recorded_and_framework_parameters_are_not()
    {
        string contract = ContractFor("""
            var orders = api.Group("Orders", g => g.Version(1));
            orders.MapPost<OrderResponse>("/", (CreateOrder body, IEndpointContext ctx) => ctx.Ok(new OrderResponse(System.Guid.Empty, "Placed")));
            """);

        Assert.Contains("request.field  POST /orders  Sample.CreateOrder.Description : string", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("IEndpointContext", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void Status_mappings_are_part_of_the_contract()
    {
        string contract = ContractFor("""
            var orders = api.Group("Orders", g => g.Version(1));
            orders.MapGet<OrderResponse>("/", (IEndpointContext ctx) => ctx.Ok(new OrderResponse(System.Guid.Empty, "Placed")));
            """);

        Assert.Contains("status  ErrorKind.NotFound = 404", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void Nested_groups_contribute_their_route_segment_and_inherit_the_version()
    {
        string contract = ContractFor("""
            var orders = api.Group("Orders", g => g.Version(2));
            var admin = orders.Group("Admin");
            admin.MapGet<OrderResponse>("/{id:guid}", (System.Guid id, IEndpointContext ctx) => ctx.Ok(new OrderResponse(id, "Placed")));
            """);

        Assert.Contains("BRAMA-CONTRACT-BEGIN Admin.v2", contract, StringComparison.Ordinal);
        Assert.Contains("endpoint  GET /orders/admin/{id:guid}", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void BaseRoute_is_reflected_in_the_contracted_route()
    {
        string contract = ContractFor("""
            var v2 = api.Group("OrdersV2", g => g.BaseRoute("orders").Version(2));
            v2.MapGet<OrderResponse>("/{id:guid}", (System.Guid id, IEndpointContext ctx) => ctx.Ok(new OrderResponse(id, "Placed")));
            """);

        Assert.Contains("endpoint  GET /orders/{id:guid}", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unversioned_group_is_never_contracted()
    {
        string contract = ContractFor("""
            var orders = api.Group("Orders");
            orders.MapGet<OrderResponse>("/", (IEndpointContext ctx) => ctx.Ok(new OrderResponse(System.Guid.Empty, "Placed")));
            """);

        Assert.DoesNotContain("BRAMA-CONTRACT-BEGIN", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void An_accepted_breaking_change_is_carried_through_to_the_build_step()
    {
        string contract = ContractFor("""
            var orders = api.Group("Orders", g => g.Version(1).AcceptBreakingChange(reason: "Per ADR-042."));
            orders.MapGet<OrderResponse>("/", (IEndpointContext ctx) => ctx.Ok(new OrderResponse(System.Guid.Empty, "Placed")));
            """);

        Assert.Contains("BRAMA-ACCEPT-BREAKING Per ADR-042.", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void The_contract_is_stable_across_runs()
    {
        const string configuration = """
            var orders = api.Group("Orders", g => g.Version(1));
            orders.MapPost<OrderResponse>("/", (CreateOrder body, IEndpointContext ctx) => ctx.Ok(new OrderResponse(System.Guid.Empty, "Placed")));
            orders.MapGet<OrderResponse>("/{id:guid}", (System.Guid id, IEndpointContext ctx) => ctx.Ok(new OrderResponse(id, "Placed")));
            """;

        Assert.Equal(ContractFor(configuration), ContractFor(configuration));
    }

    private static string ContractFor(string configuration)
    {
        GeneratorRunResult result = GeneratorTestHelper.Run(new BramaGenerator(), $$"""
            using Brama;
            using Brama.Configuration;
            using Microsoft.AspNetCore.Http;

            namespace Sample;

            public record OrderResponse(System.Guid Id, string Status);
            public record CreateOrder(string Description);

            public static class Api
            {
                public static void Build()
                {
                    var api = new ApiConfig();
                    api.ProblemDetails(p => p
                        .Map(ErrorKind.Validation, 400)
                        .Map(ErrorKind.NotFound, 404)
                        .Map(ErrorKind.Conflict, 409)
                        .Map(ErrorKind.Unauthorized, 401)
                        .Map(ErrorKind.Forbidden, 403)
                        .Map(ErrorKind.Problem, 400));

                    {{configuration}}
                }
            }
            """);

        return result.GetGeneratedSource("Brama.Contract.g.cs");
    }
}
