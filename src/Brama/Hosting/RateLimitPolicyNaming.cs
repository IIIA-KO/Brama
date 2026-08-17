using System.Globalization;
using Brama.Configuration;

namespace Brama.Hosting;

/// <summary>
/// Names the ASP.NET Core rate-limiting policy that an inline
/// <c>RateLimit(permitLimit:, window:)</c> declaration is registered under.
/// </summary>
/// <remarks>
/// Derived from the limit itself rather than from the declaring node, so two groups asking for the
/// same limit share one registration instead of racing to register duplicates.
/// </remarks>
internal static class RateLimitPolicyNaming
{
    public static string NameFor(RateLimitPolicySpec spec) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"brama:rl:{spec.PermitLimit}:{spec.Window.Ticks}");
}
