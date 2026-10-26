using System.Globalization;
using FluentValidation;
using MediatR;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Money;

namespace Pena_e_Arte.Application.Public.Queries;

/// <summary>
/// The newest Bank of Albania EUR/ALL official rate, for the approximate lek amount shown next to euro
/// prices before a client pays (Albanian EUR studios only). Null when no rate has ever been stored.
/// Public reference data, no tenant or user data touched; anonymous because guests see prices on the
/// booking, artist and gift-card pages. See architecture.md's "AllowAnonymous Exceptions" table.
/// </summary>
public record GetLatestEurAllRateQuery : IRequest<ExchangeRateResponse?>;

public class GetLatestEurAllRateHandler(IExchangeRateProvider rates)
    : IRequestHandler<GetLatestEurAllRateQuery, ExchangeRateResponse?>
{
    public async Task<ExchangeRateResponse?> Handle(GetLatestEurAllRateQuery query, CancellationToken ct)
    {
        ExchangeRateSnapshot? latest = await rates.GetLatestAsync(
            ReferenceAmount.BaseCurrency, ReferenceAmount.ReferenceCurrency, ct);

        return latest is null
            ? null
            : new ExchangeRateResponse(
                latest.Rate,
                latest.RateDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                latest.Source,
                latest.IsStale);
    }
}

// The query carries no user-supplied value, so there is nothing to validate; the validator exists to
// keep the "every request has a validator" convention uniform.
public class GetLatestEurAllRateValidator : AbstractValidator<GetLatestEurAllRateQuery>
{
}
