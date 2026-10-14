using Xunit;

namespace Brama.SourceGenerator.MSBuildTests;

/// <summary>
/// Drives the Contracts build step the way a consumer does: by running <c>dotnet build</c> against a
/// real project. These are slow, and they are the only tests that cover the differentiator's actual
/// enforcement rather than the surface model behind it.
/// </summary>
[Trait("Category", "MSBuild")]
public class ContractBuildStepTests
{
    private const string OneEndpoint = """
        var orders = api.Group("Orders", g => g.Version(1));
        orders.MapGet<OrderResponse>("/{id:guid}", (Guid id, IEndpointContext ctx) => ctx.Ok(new OrderResponse(id, "Placed")));
        """;

    private const string Types = "public record OrderResponse(Guid Id, string Status);";

    [Fact]
    public void The_first_real_build_freezes_the_contract()
    {
        using var fixture = new ContractFixture();
        fixture.WriteProgram(OneEndpoint, Types);

        BuildResult build = fixture.Build();

        Assert.True(build.Succeeded, build.Output);
        Assert.True(fixture.ContractExists("Orders.v1"));
        Assert.Contains("response.field  GET /orders/{id:guid}  Status : string", fixture.ReadContract("Orders.v1"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_design_time_build_never_touches_the_committed_contract()
    {
        using var fixture = new ContractFixture();
        fixture.WriteProgram(OneEndpoint, Types);
        BuildResult references = fixture.BuildReferences();
        Assert.True(references.Succeeded, references.Output);

        BuildResult build = fixture.Build("-p:DesignTimeBuild=true");

        Assert.True(build.Succeeded, build.Output);
        Assert.False(
            fixture.ContractExists("Orders.v1"),
            "A design-time build wrote the contract. This is the invariant the generator/target split exists to protect.");
    }

    [Fact]
    public void Rebuilding_an_unchanged_project_leaves_the_contract_byte_identical()
    {
        using var fixture = new ContractFixture();
        fixture.WriteProgram(OneEndpoint, Types);

        Assert.True(fixture.Build().Succeeded);
        string frozen = fixture.ReadContract("Orders.v1");

        Assert.True(fixture.Build().Succeeded);

        Assert.Equal(frozen, fixture.ReadContract("Orders.v1"));
    }

    [Fact]
    public void Adding_a_field_updates_the_contract_without_failing_the_build()
    {
        using var fixture = new ContractFixture();
        fixture.WriteProgram(OneEndpoint, Types);
        Assert.True(fixture.Build().Succeeded);

        fixture.WriteProgram(OneEndpoint, "public record OrderResponse(Guid Id, string Status, string? Note = null);");
        BuildResult build = fixture.Build();

        Assert.True(build.Succeeded, build.Output);
        Assert.Contains("Note : string?", fixture.ReadContract("Orders.v1"), StringComparison.Ordinal);
    }

    [Fact]
    public void Adding_an_endpoint_to_a_frozen_version_is_not_a_breaking_change()
    {
        using var fixture = new ContractFixture();
        fixture.WriteProgram(OneEndpoint, Types);
        Assert.True(fixture.Build().Succeeded);

        fixture.WriteProgram(
            OneEndpoint + """

            orders.MapGet<OrderResponse>("/latest", (IEndpointContext ctx) => ctx.Ok(new OrderResponse(Guid.Empty, "Placed")));
            """,
            Types);

        BuildResult build = fixture.Build();

        Assert.True(build.Succeeded, build.Output);
        Assert.Contains("endpoint  GET /orders/latest", fixture.ReadContract("Orders.v1"), StringComparison.Ordinal);
    }

    [Fact]
    public void Renaming_a_response_field_fails_the_build_and_names_what_was_lost()
    {
        using var fixture = new ContractFixture();
        fixture.WriteProgram(OneEndpoint, Types);
        Assert.True(fixture.Build().Succeeded);

        fixture.WriteProgram(
            OneEndpoint.Replace("\"Placed\"", "\"Placed\"", StringComparison.Ordinal),
            "public record OrderResponse(Guid Id, string State);");

        BuildResult build = fixture.Build();

        Assert.False(build.Succeeded, "A breaking change to a frozen version must fail the build.");
        Assert.Contains("BRAMA010", build.Output, StringComparison.Ordinal);
        Assert.Contains("response.field  GET /orders/{id:guid}  Status : string", build.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Removing_an_endpoint_fails_the_build()
    {
        using var fixture = new ContractFixture();
        fixture.WriteProgram(
            OneEndpoint + """

            orders.MapGet<OrderResponse>("/latest", (IEndpointContext ctx) => ctx.Ok(new OrderResponse(Guid.Empty, "Placed")));
            """,
            Types);
        Assert.True(fixture.Build().Succeeded);

        fixture.WriteProgram(OneEndpoint, Types);
        BuildResult build = fixture.Build();

        Assert.False(build.Succeeded);
        Assert.Contains("endpoint  GET /orders/latest", build.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_status_code_remapping_fails_the_build()
    {
        using var fixture = new ContractFixture();
        fixture.WriteProgram(OneEndpoint, Types);
        Assert.True(fixture.Build().Succeeded);

        fixture.WriteProgram(OneEndpoint.Replace("Version(1)", "Version(1)", StringComparison.Ordinal), Types);
        RewriteStatusMapping(fixture);

        BuildResult build = fixture.Build();

        Assert.False(build.Succeeded);
        Assert.Contains("status  ErrorKind.Conflict = 409", build.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptBreakingChange_lets_the_build_through_and_rewrites_the_contract()
    {
        using var fixture = new ContractFixture();
        fixture.WriteProgram(OneEndpoint, Types);
        Assert.True(fixture.Build().Succeeded);

        fixture.WriteProgram(
            """
            var orders = api.Group("Orders", g => g.Version(1).AcceptBreakingChange(reason: "Renamed before any client shipped."));
            orders.MapGet<OrderResponse>("/{id:guid}", (Guid id, IEndpointContext ctx) => ctx.Ok(new OrderResponse(id, "Placed")));
            """,
            "public record OrderResponse(Guid Id, string State);");

        BuildResult build = fixture.Build();

        Assert.True(build.Succeeded, build.Output);
        Assert.Contains("Renamed before any client shipped.", build.Output, StringComparison.Ordinal);

        string contract = fixture.ReadContract("Orders.v1");
        Assert.Contains("State : string", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("Status : string", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void A_group_with_no_version_is_never_contracted()
    {
        using var fixture = new ContractFixture();
        fixture.WriteProgram(
            """
            var orders = api.Group("Orders");
            orders.MapGet<OrderResponse>("/{id:guid}", (Guid id, IEndpointContext ctx) => ctx.Ok(new OrderResponse(id, "Placed")));
            """,
            Types);

        BuildResult build = fixture.Build();

        Assert.True(build.Succeeded, build.Output);
        Assert.False(fixture.ContractExists("Orders.v1"));
    }

    /// <summary>Drops the Conflict mapping's status code to a different value.</summary>
    private static void RewriteStatusMapping(ContractFixture fixture)
    {
        string path = Path.Combine(Path.GetDirectoryName(fixture.ContractPath("Orders.v1"))!, "..", "Program.cs");
        string program = File.ReadAllText(Path.GetFullPath(path));

        File.WriteAllText(
            Path.GetFullPath(path),
            program.Replace(".Map(ErrorKind.Conflict, 409)", ".Map(ErrorKind.Conflict, 422)", StringComparison.Ordinal));
    }
}
