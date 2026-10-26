using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Public;

public class GetLatestEurAllRateHandlerTests
{
    private readonly IExchangeRateProvider _rates = Substitute.For<IExchangeRateProvider>();

    [Fact]
    public async Task Handle_NoRateStored_ReturnsNull()
    {
        _rates.GetLatestAsync("EUR", "ALL", Arg.Any<CancellationToken>()).Returns((ExchangeRateSnapshot?)null);

        ExchangeRateResponse? result = await new GetLatestEurAllRateHandler(_rates).Handle(new GetLatestEurAllRateQuery(), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_RateStored_MapsItWithAnIsoDate()
    {
        _rates.GetLatestAsync("EUR", "ALL", Arg.Any<CancellationToken>())
            .Returns(new ExchangeRateSnapshot(90.74m, new DateOnly(2026, 10, 23), "BoA-Fix", IsStale: true));

        ExchangeRateResponse? result = await new GetLatestEurAllRateHandler(_rates).Handle(new GetLatestEurAllRateQuery(), default);

        result.Should().Be(new ExchangeRateResponse(90.74m, "2026-10-23", "BoA-Fix", IsStale: true));
    }
}
