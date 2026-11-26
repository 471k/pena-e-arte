using System.Text.Json;
using FluentAssertions;
using Pena_e_Arte.Application.BoothRent;
using Pena_e_Arte.Contracts.Requests;

namespace Pena_e_Arte.UnitTests.BoothRent;

// The booth-rent write path takes a calendar date, not an instant. Before 26 Nov 2026 the two request
// records carried a DateTime, so "2026-12-01T00:00:00+01:00" bound as Kind=Local and was stored as
// 2026-11-30T23:00Z, which the screens then printed as 30 November. These tests pin the contract at the
// boundary, with the same serializer options minimal APIs bind request bodies with (the API configures none
// of its own), so a time or an offset can no longer reach a handler.
public class BoothRentScheduleDateContractTests
{
    private static readonly JsonSerializerOptions MinimalApiBodyOptions =
        new Microsoft.AspNetCore.Http.Json.JsonOptions().SerializerOptions;

    private static string CreateBody(string nextChargeDate) =>
        $$"""{"artistId":"{{Guid.NewGuid()}}","amountFixed":120,"frequency":"Monthly","nextChargeDate":"{{nextChargeDate}}","isActive":true}""";

    private static string UpdateBody(string nextChargeDate) =>
        $$"""{"amountFixed":120,"frequency":"Monthly","nextChargeDate":"{{nextChargeDate}}","isActive":true}""";

    [Fact]
    public void CreateRequest_BindsAPlainCalendarDate()
    {
        CreateBoothRentScheduleRequest? request =
            JsonSerializer.Deserialize<CreateBoothRentScheduleRequest>(CreateBody("2026-12-01"), MinimalApiBodyOptions);

        request!.NextChargeDate.Should().Be(new DateOnly(2026, 12, 1));
    }

    [Fact]
    public void UpdateRequest_BindsAPlainCalendarDate()
    {
        UpdateBoothRentScheduleRequest? request =
            JsonSerializer.Deserialize<UpdateBoothRentScheduleRequest>(UpdateBody("2026-12-01"), MinimalApiBodyOptions);

        request!.NextChargeDate.Should().Be(new DateOnly(2026, 12, 1));
    }

    // The original bug's input is first. A JsonException in body binding is a 400: minimal APIs answer it
    // themselves in production, and ExceptionMiddleware maps BadHttpRequestException and JsonException to 400.
    [Theory]
    [InlineData("2026-12-01T00:00:00+01:00")]
    [InlineData("2026-12-01T15:30:00Z")]
    [InlineData("2026-12-01T00:00:00.000Z")]
    [InlineData("2026-12-01T00:00:00")]
    public void BothRequests_RejectAValueThatCarriesATime(string instant)
    {
        Action create = () => JsonSerializer.Deserialize<CreateBoothRentScheduleRequest>(CreateBody(instant), MinimalApiBodyOptions);
        Action update = () => JsonSerializer.Deserialize<UpdateBoothRentScheduleRequest>(UpdateBody(instant), MinimalApiBodyOptions);

        create.Should().Throw<JsonException>();
        update.Should().Throw<JsonException>();
    }

    [Fact]
    public void ToChargeDate_IsThatCalendarDaysMidnightUtc()
    {
        DateTime stored = BoothRentDates.ToChargeDate(new DateOnly(2026, 12, 1));

        stored.Should().Be(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        stored.Kind.Should().Be(DateTimeKind.Utc);
    }
}
