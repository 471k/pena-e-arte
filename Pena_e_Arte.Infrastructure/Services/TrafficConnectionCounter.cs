using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

public class TrafficConnectionCounter : ITrafficConnectionCounter
{
    private int _count;

    public int Count => _count;

    public void Increment() => Interlocked.Increment(ref _count);

    public void Decrement() => Interlocked.Decrement(ref _count);
}
