using Microsoft.Extensions.DependencyInjection;

namespace Brama;

/// <summary>
/// Marks a partial class as a module, for which Brama generates an
/// <c>Add{ClassName}(IServiceCollection)</c> extension method.
/// </summary>
/// <remarks>
/// The scan the method performs happens at compile time, so a module registers its services with
/// no assembly scanning, no reflection, and nothing to keep in sync by hand.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ModuleAttribute : Attribute;

/// <summary>
/// Declares that every concrete implementation of <see cref="ServiceType"/> in this assembly should
/// be registered on the module's behalf.
/// </summary>
/// <remarks>
/// Open generics are supported: <c>[RegisterAll(typeof(IDomainEventHandler&lt;&gt;))]</c> registers
/// each closed implementation against the interface it actually implements. This is the compile-time
/// answer to the block of hand-copied reflection registration that tends to appear, identical, in
/// every module of a codebase.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RegisterAllAttribute : Attribute
{
    /// <param name="serviceType">The interface to find implementations of. May be an open generic.</param>
    public RegisterAllAttribute(Type serviceType) => ServiceType = serviceType;

    /// <summary>The interface implementations are registered against.</summary>
    public Type ServiceType { get; }

    /// <summary>The lifetime each implementation is registered with. Scoped by default.</summary>
    public ServiceLifetime Lifetime { get; init; } = ServiceLifetime.Scoped;
}
