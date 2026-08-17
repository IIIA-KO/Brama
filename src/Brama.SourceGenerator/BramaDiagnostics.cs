using Microsoft.CodeAnalysis;

namespace Brama.SourceGenerator;

/// <summary>
/// The diagnostics Brama reports at build time.
/// </summary>
internal static class BramaDiagnostics
{
    private const string Category = "Brama";

    /// <summary>
    /// An <c>ErrorKind</c> has no HTTP status code mapped, so a handler returning it would fall
    /// through to whatever status happened to be the default — the exact class of bug this library
    /// exists to close.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingErrorKindMapping = new(
        id: "BRAMA001",
        title: "Every ErrorKind must be mapped to an HTTP status code",
        messageFormat: "No HTTP status code is mapped for {0}. Add .Map({0}, …) to this ProblemDetails configuration.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "ErrorKind is a closed enum, so the set of mappings a correct configuration needs is known at "
            + "compile time. A missing mapping is reported here rather than becoming a wrong status code at runtime.");

    /// <summary>
    /// The configuration could not be read statically, so no exhaustiveness claim can be made about
    /// it. Reported rather than passed over silently: looking enforced without being enforced is
    /// worse than not being checked at all.
    /// </summary>
    public static readonly DiagnosticDescriptor ConfigurationNotStaticallyAnalyzable = new(
        id: "BRAMA011",
        title: "Brama configuration could not be analyzed at build time",
        messageFormat: "The configuration is passed to '{0}', so its ErrorKind mappings cannot be checked at build time. They are still verified when the application starts.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description:
            "Brama's build-time checks read source syntax. Configuration assembled somewhere the generator "
            + "cannot follow — another method, a loop, a runtime conditional — is checked by the startup guard "
            + "in AddBrama instead, which throws rather than serving wrong status codes.");
}
