using FluentAssertions;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.UnitTests.Infrastructure.Services;

public class TrafficConnectionCounterTests
{
    [Fact]
    public void Count_NoActivity_IsZero()
    {
        var sut = new TrafficConnectionCounter();

        sut.Count.Should().Be(0);
    }

    [Fact]
    public void Increment_ThenDecrement_ReturnsToPreviousCount()
    {
        var sut = new TrafficConnectionCounter();

        sut.Increment();
        sut.Increment();
        sut.Decrement();

        sut.Count.Should().Be(1);
    }
}
