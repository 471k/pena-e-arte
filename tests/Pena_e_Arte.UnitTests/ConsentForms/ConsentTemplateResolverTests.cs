using FluentAssertions;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.UnitTests.ConsentForms;

public class ConsentTemplateResolverTests
{
    private static readonly DateTime Now = new(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc);
    private readonly Guid _studio = Guid.NewGuid();

    private ConsentTemplate Template(
        Guid? studioId, bool active, DateTime effectiveFrom,
        ConsentTemplateKind kind = ConsentTemplateKind.AppointmentConsent, string body = "body") =>
        new()
        {
            StudioId = studioId,
            IsActive = active,
            EffectiveFrom = effectiveFrom,
            Kind = kind,
            BodyText = body,
        };

    [Fact]
    public void ResolveActive_PrefersStudioTemplateOverPlatformDefault()
    {
        List<ConsentTemplate> candidates =
        [
            Template(null, true, Now.AddDays(-5), body: "default"),
            Template(_studio, true, Now.AddDays(-1), body: "studio"),
        ];

        ConsentTemplate? result = ConsentTemplateResolver.ResolveActive(
            candidates, _studio, ConsentTemplateKind.AppointmentConsent, Now);

        result!.BodyText.Should().Be("studio");
    }

    [Fact]
    public void ResolveActive_FallsBackToPlatformDefault_WhenNoStudioTemplate()
    {
        List<ConsentTemplate> candidates = [Template(null, true, Now.AddDays(-5), body: "default")];

        ConsentTemplate? result = ConsentTemplateResolver.ResolveActive(
            candidates, _studio, ConsentTemplateKind.AppointmentConsent, Now);

        result!.BodyText.Should().Be("default");
    }

    [Fact]
    public void ResolveActive_IgnoresInactiveAndFutureTemplates()
    {
        List<ConsentTemplate> candidates =
        [
            Template(_studio, active: false, Now.AddDays(-1), body: "inactive"),
            Template(_studio, active: true, Now.AddDays(1), body: "future"),
        ];

        ConsentTemplate? result = ConsentTemplateResolver.ResolveActive(
            candidates, _studio, ConsentTemplateKind.AppointmentConsent, Now);

        result.Should().BeNull();
    }

    [Fact]
    public void ResolveActive_PicksMostRecentEffectiveFrom()
    {
        List<ConsentTemplate> candidates =
        [
            Template(_studio, true, Now.AddDays(-10), body: "old"),
            Template(_studio, true, Now.AddDays(-2), body: "new"),
        ];

        ConsentTemplate? result = ConsentTemplateResolver.ResolveActive(
            candidates, _studio, ConsentTemplateKind.AppointmentConsent, Now);

        result!.BodyText.Should().Be("new");
    }

    [Fact]
    public void ResolveActive_ReturnsNull_WhenNoEligibleTemplate()
    {
        ConsentTemplate? result = ConsentTemplateResolver.ResolveActive(
            [], _studio, ConsentTemplateKind.AppointmentConsent, Now);

        result.Should().BeNull();
    }

    [Fact]
    public void ResolveActive_RespectsKind()
    {
        List<ConsentTemplate> candidates =
        [
            Template(_studio, true, Now.AddDays(-1),
                kind: ConsentTemplateKind.CrossTenantProfileSharing, body: "health"),
        ];

        ConsentTemplate? appointment = ConsentTemplateResolver.ResolveActive(
            candidates, _studio, ConsentTemplateKind.AppointmentConsent, Now);
        ConsentTemplate? health = ConsentTemplateResolver.ResolveActive(
            candidates, _studio, ConsentTemplateKind.CrossTenantProfileSharing, Now);

        appointment.Should().BeNull();
        health!.BodyText.Should().Be("health");
    }
}
