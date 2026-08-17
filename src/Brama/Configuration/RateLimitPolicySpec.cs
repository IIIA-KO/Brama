namespace Brama.Configuration;

/// <summary>
/// A rate-limit policy authored inline in the config tree, next to the endpoints it protects,
/// instead of registered separately against <c>AddRateLimiter</c>.
/// </summary>
/// <param name="PermitLimit">How many requests are permitted per <paramref name="Window"/>.</param>
/// <param name="Window">The fixed window the limit applies over.</param>
public sealed record RateLimitPolicySpec(int PermitLimit, TimeSpan Window);
