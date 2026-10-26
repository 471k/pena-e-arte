using FluentValidation;
using MediatR;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Money;

namespace Pena_e_Arte.Application.Public.Queries;

/// <summary>
/// Wraps StudioCurrencies.DefaultFor for the registration/sign-up forms — the frontend has
/// no country->currency table of its own (deliberately, per docs/claude/architecture.md Decisions
/// Log, "Studio currency"), so it asks the server. Always ALL or EUR, never null since
/// 2026-10-26 (the field stays nullable for older clients). Static reference data only; no tenant
/// or user data touched. See architecture.md's "AllowAnonymous Exceptions" table.
/// </summary>
public record GetCountryDefaultCurrencyQuery(string CountryCode) : IRequest<CountryDefaultCurrencyResponse>;

public class GetCountryDefaultCurrencyHandler : IRequestHandler<GetCountryDefaultCurrencyQuery, CountryDefaultCurrencyResponse>
{
    public Task<CountryDefaultCurrencyResponse> Handle(GetCountryDefaultCurrencyQuery query, CancellationToken ct)
    {
        string countryCode = query.CountryCode.ToUpperInvariant();
        string currency = StudioCurrencies.DefaultFor(countryCode);
        return Task.FromResult(new CountryDefaultCurrencyResponse(countryCode, currency));
    }
}

public class GetCountryDefaultCurrencyValidator : AbstractValidator<GetCountryDefaultCurrencyQuery>
{
    public GetCountryDefaultCurrencyValidator()
    {
        RuleFor(x => x.CountryCode)
            .NotEmpty()
            .Length(2)
            .Matches("^[A-Za-z]{2}$");
    }
}
