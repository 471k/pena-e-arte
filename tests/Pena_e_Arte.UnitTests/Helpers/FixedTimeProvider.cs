namespace Pena_e_Arte.UnitTests.Helpers;

/// <summary>A clock frozen at one instant, for code that takes a TimeProvider.</summary>
public sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
