using FluentAssertions;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.UnitTests.Services;

/// <summary>E05b-2: the release deadline the job acts on and the owner's unsettled view shows, from one place.</summary>
public class PaymentReleaseRuleTests
{
    private static readonly DateTime Appointment = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void HoldExpiresAt_WhenSet_IsTheDeadline_WhateverTheAppointmentDate()
    {
        DateTime expires = new(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);

        PaymentReleaseRule.DeadlineFor(expires, Appointment).Should().Be(expires);
    }

    [Fact]
    public void LegacyRowWithoutExpiry_IsReleasedThreeDaysAfterItsAppointment()
    {
        PaymentReleaseRule.DeadlineFor(null, Appointment).Should().Be(Appointment.AddDays(3));
    }
}
