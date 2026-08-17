using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Http;

namespace Brama.Http;

/// <summary>
/// Runs field-level DataAnnotations checks on bound request objects before the handler sees them.
/// </summary>
/// <remarks>
/// Deliberately narrow: single-field, declarative constraints only. Business rules — cross-field
/// invariants, anything needing the domain — belong to the application's own validation layer, and
/// a handler reports those with <c>ctx.Invalid(...)</c> through this same response shape.
/// <para>
/// There is no exception anywhere in this path. A failure becomes an <see cref="ErrorResult"/>
/// directly, so it cannot be caught by an outer handler and re-reported as some unrelated error —
/// which is exactly how validation failures used to surface as 500s in the codebase this library
/// was designed against.
/// </para>
/// </remarks>
internal sealed class ValidationFilter : IEndpointFilter
{
    private static readonly ConcurrentDictionary<Type, bool> Validatable = new();

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        List<ValidationFailure>? failures = null;

        foreach (object? argument in context.Arguments)
        {
            if (argument is null || !IsValidatable(argument.GetType()))
            {
                continue;
            }

            List<ValidationResult> results = [];
            if (Validator.TryValidateObject(argument, new ValidationContext(argument), results, validateAllProperties: true))
            {
                continue;
            }

            failures ??= [];
            foreach (ValidationResult result in results)
            {
                string field = result.MemberNames.FirstOrDefault() ?? string.Empty;
                failures.Add(new ValidationFailure(field, result.ErrorMessage ?? "Invalid value."));
            }
        }

        return failures is null
            ? next(context)
            : ValueTask.FromResult<object?>(Result.Invalid("The request is not valid.", failures));
    }

    /// <summary>
    /// Whether a bound argument is worth validating: it carries at least one validation attribute.
    /// That single question is the whole filter — no guessing from assembly or namespace names,
    /// which is what would make a request type in an assembly named like Brama's own quietly skip
    /// validation. Framework types (the endpoint context, cancellation tokens, injected services)
    /// carry no such attributes, so they fall out for free.
    /// </summary>
    private static bool IsValidatable(Type type) => Validatable.GetOrAdd(type, static candidate =>
        !candidate.IsPrimitive
        && candidate != typeof(string)
        && !candidate.IsEnum
        && candidate
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(property => property.GetCustomAttributes<ValidationAttribute>(inherit: true).Any()));
}
