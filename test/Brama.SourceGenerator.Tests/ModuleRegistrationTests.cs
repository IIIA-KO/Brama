using Xunit;

namespace Brama.SourceGenerator.Tests;

/// <summary>
/// Covers the <c>[Module]</c> marker: the compile-time replacement for the identical
/// reflection-scanning registration block that gets hand-copied into every module of a codebase.
/// </summary>
public class ModuleRegistrationTests
{
    [Fact]
    public void A_module_gets_an_Add_extension_method()
    {
        GeneratorRunResult result = Run("""
            using Brama;

            namespace Sample;

            [Module]
            public partial class OrdersModule;
            """);

        Assert.Empty(result.Errors);

        string generated = result.GetGeneratedSource("OrdersModule.Module.g.cs");
        Assert.Contains("public static IServiceCollection AddOrdersModule(this IServiceCollection services)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Implementations_are_written_out_as_literal_registrations()
    {
        GeneratorRunResult result = Run("""
            using Brama;
            using Microsoft.Extensions.DependencyInjection;

            namespace Sample;

            public interface IDomainEventHandler<TEvent> { }

            public record OrderPlaced;
            public record OrderCancelled;

            public class OrderPlacedHandler : IDomainEventHandler<OrderPlaced> { }
            public class OrderCancelledHandler : IDomainEventHandler<OrderCancelled> { }

            [Module]
            [RegisterAll(typeof(IDomainEventHandler<>))]
            public partial class OrdersModule;
            """);

        Assert.Empty(result.Errors);

        string generated = result.GetGeneratedSource("OrdersModule.Module.g.cs");
        Assert.Contains(
            "services.AddScoped(typeof(Sample.IDomainEventHandler<Sample.OrderPlaced>), typeof(Sample.OrderPlacedHandler));",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "services.AddScoped(typeof(Sample.IDomainEventHandler<Sample.OrderCancelled>), typeof(Sample.OrderCancelledHandler));",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void No_reflection_appears_anywhere_in_the_generated_registration()
    {
        GeneratorRunResult result = Run("""
            using Brama;

            namespace Sample;

            public interface IHandler { }
            public class Handler : IHandler { }

            [Module]
            [RegisterAll(typeof(IHandler))]
            public partial class OrdersModule;
            """);

        string generated = result.GetGeneratedSource("OrdersModule.Module.g.cs");

        Assert.DoesNotContain("Assembly", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("GetTypes", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void The_declared_lifetime_is_the_one_emitted()
    {
        GeneratorRunResult result = Run("""
            using Brama;
            using Microsoft.Extensions.DependencyInjection;

            namespace Sample;

            public interface IHandler { }
            public class Handler : IHandler { }

            [Module]
            [RegisterAll(typeof(IHandler), Lifetime = ServiceLifetime.Singleton)]
            public partial class OrdersModule;
            """);

        string generated = result.GetGeneratedSource("OrdersModule.Module.g.cs");

        Assert.Contains("services.AddSingleton(typeof(Sample.IHandler), typeof(Sample.Handler));", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Abstract_types_are_not_registered()
    {
        GeneratorRunResult result = Run("""
            using Brama;

            namespace Sample;

            public interface IHandler { }
            public abstract class HandlerBase : IHandler { }
            public class RealHandler : HandlerBase { }

            [Module]
            [RegisterAll(typeof(IHandler))]
            public partial class OrdersModule;
            """);

        string generated = result.GetGeneratedSource("OrdersModule.Module.g.cs");

        Assert.DoesNotContain("typeof(Sample.HandlerBase)", generated, StringComparison.Ordinal);
        Assert.Contains("typeof(Sample.RealHandler)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_closed_generic_request_registers_only_that_one_implementation()
    {
        GeneratorRunResult result = Run("""
            using Brama;

            namespace Sample;

            public interface IHandler<TEvent> { }

            public record Placed;
            public record Cancelled;

            public class PlacedHandler : IHandler<Placed> { }
            public class CancelledHandler : IHandler<Cancelled> { }

            [Module]
            [RegisterAll(typeof(IHandler<Placed>))]
            public partial class OrdersModule;
            """);

        string generated = result.GetGeneratedSource("OrdersModule.Module.g.cs");

        Assert.Contains("typeof(Sample.PlacedHandler)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(Sample.CancelledHandler)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_type_handling_two_events_is_registered_against_both()
    {
        GeneratorRunResult result = Run("""
            using Brama;

            namespace Sample;

            public interface IHandler<TEvent> { }

            public record Placed;
            public record Cancelled;

            public class BothHandler : IHandler<Placed>, IHandler<Cancelled> { }

            [Module]
            [RegisterAll(typeof(IHandler<>))]
            public partial class OrdersModule;
            """);

        string generated = result.GetGeneratedSource("OrdersModule.Module.g.cs");

        Assert.Contains("typeof(Sample.IHandler<Sample.Placed>), typeof(Sample.BothHandler)", generated, StringComparison.Ordinal);
        Assert.Contains("typeof(Sample.IHandler<Sample.Cancelled>), typeof(Sample.BothHandler)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Several_RegisterAll_attributes_on_one_module_are_all_honoured()
    {
        GeneratorRunResult result = Run("""
            using Brama;
            using Microsoft.Extensions.DependencyInjection;

            namespace Sample;

            public interface IReader { }
            public interface IWriter { }

            public class Reader : IReader { }
            public class Writer : IWriter { }

            [Module]
            [RegisterAll(typeof(IReader))]
            [RegisterAll(typeof(IWriter), Lifetime = ServiceLifetime.Transient)]
            public partial class OrdersModule;
            """);

        string generated = result.GetGeneratedSource("OrdersModule.Module.g.cs");

        Assert.Contains("services.AddScoped(typeof(Sample.IReader), typeof(Sample.Reader));", generated, StringComparison.Ordinal);
        Assert.Contains("services.AddTransient(typeof(Sample.IWriter), typeof(Sample.Writer));", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_class_without_the_marker_gets_nothing()
    {
        GeneratorRunResult result = Run("""
            namespace Sample;

            public partial class NotAModule;
            """);

        Assert.DoesNotContain(
            result.OutputCompilation.SyntaxTrees,
            tree => tree.FilePath.EndsWith("NotAModule.Module.g.cs", StringComparison.Ordinal));
    }

    private static GeneratorRunResult Run(string source) => GeneratorTestHelper.Run(new BramaGenerator(), source);
}
