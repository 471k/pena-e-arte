using System.Net;
using FluentValidation;
using MediatR;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Public.Queries;

/// <summary>
/// Resolves the visitor's country and timezone from their IP through the existing GeoIP database,
/// so the registration and studio-settings forms can default the country (and with it the currency
/// and phone prefix) and the timezone to where the person actually is instead of guessing from the
/// browser language. Only the two-letter country code and the IANA timezone id leave this handler:
/// the IP is never stored or logged here. See architecture.md's "AllowAnonymous Exceptions" table.
/// </summary>
public record GetVisitorGeoQuery(IPAddress? Ip) : IRequest<VisitorGeoResponse>;

public class GetVisitorGeoHandler(IGeoIpService geoIp)
    : IRequestHandler<GetVisitorGeoQuery, VisitorGeoResponse>
{
    public Task<VisitorGeoResponse> Handle(GetVisitorGeoQuery query, CancellationToken ct)
    {
        if (query.Ip is null)
            return Task.FromResult(new VisitorGeoResponse(null, null));

        GeoIpResult? geo = geoIp.Lookup(query.Ip);

        string? country = geo?.CountryCode is { Length: 2 } code ? code.ToUpperInvariant() : null;
        string? timeZone = string.IsNullOrWhiteSpace(geo?.TimeZone) ? null : geo!.TimeZone;

        return Task.FromResult(new VisitorGeoResponse(country, timeZone));
    }
}

// The query carries no user-supplied value (the IP comes from the connection, not the request),
// so there is nothing to validate; the validator exists to keep the "every request has a
// validator" convention uniform.
public class GetVisitorGeoValidator : AbstractValidator<GetVisitorGeoQuery>
{
}
