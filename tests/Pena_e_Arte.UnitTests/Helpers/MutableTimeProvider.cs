namespace Pena_e_Arte.UnitTests.Helpers;

/// <summary>A TimeProvider whose "now" the test moves by hand.</summary>
public sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
