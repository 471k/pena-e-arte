using System.Text.RegularExpressions;
using FluentAssertions;
using Pena_e_Arte.Infrastructure.Services.MailKit;

namespace Pena_e_Arte.UnitTests.Services;

/// <summary>
/// Brand master 1.0.0 email palette (follow-up to #475, Phi's 2.3 answer (b)). The inline-markup emails had
/// a #7c3aed heading (10) and #7c3aed action buttons (8). Headings are now ink #19161D on the white email body
/// (17.89:1) and buttons are violet #863BFF with paper #F5F1E8 text (4.56:1); both pass WCAG AA 4.5:1 for
/// normal text. Violet as heading TEXT was not used: it is 5.14:1 on white but 3.48:1 where a client darkens the
/// body to ink. One case per template that carried the old colour.
/// </summary>
public class EmailRendererBrandColourTests
{
    private static readonly EmailRenderer Renderer = new();

    public static TheoryData<string, int> Templates => new()
    {
        { "Aftercare", 0 },
        { "EmailVerification", 1 },
        { "ArtistInvite", 1 },
        { "ArtistInviteRejoining", 1 },
        { "StudioJoinInvite", 1 },
        { "PasswordReset", 1 },
        { "GuestBookingWelcome", 1 },
        { "GuestBookingEmailCollision", 0 },
        { "GuestBookingEmailCollisionWithResume", 1 },
        { "ChangeEmailConfirmation", 1 },
        { "EmailChangedNotice", 0 },
    };

    private static string Render(string template) => template switch
    {
        "Aftercare" => Renderer.RenderAftercare("Marco", "Ink Studio", "Ari Kola", showBranding: true),
        "EmailVerification" => Renderer.RenderEmailVerification("https://app.tattooos.co/confirm?t=x"),
        "ArtistInvite" => Renderer.RenderArtistInvite("Ari", "Ink Studio", "https://app.tattooos.co/set?t=x"),
        "ArtistInviteRejoining" => Renderer.RenderArtistInvite("Ari", "Ink Studio", "https://app.tattooos.co/set?t=x", isRejoiningArtist: true),
        "StudioJoinInvite" => Renderer.RenderStudioJoinInvite("Ink Studio", "Tirana", "https://app.tattooos.co/invites"),
        "PasswordReset" => Renderer.RenderPasswordReset("https://app.tattooos.co/reset?t=x"),
        "GuestBookingWelcome" => Renderer.RenderGuestBookingWelcome("Ink Studio", "https://app.tattooos.co/set?t=x", "https://app.tattooos.co/confirm?t=x"),
        "GuestBookingEmailCollision" => Renderer.RenderGuestBookingEmailCollision("Ink Studio"),
        "GuestBookingEmailCollisionWithResume" => Renderer.RenderGuestBookingEmailCollision("Ink Studio", "https://app.tattooos.co/resume?t=x"),
        "ChangeEmailConfirmation" => Renderer.RenderChangeEmailConfirmation("https://app.tattooos.co/confirm-email?t=x"),
        "EmailChangedNotice" => Renderer.RenderEmailChangedNotice("new@example.com"),
        _ => throw new ArgumentOutOfRangeException(nameof(template), template, null),
    };

    [Theory]
    [MemberData(nameof(Templates))]
    public void Template_HasNoOldViolet_AndUsesTheBrandPalette(string template, int expectedButtons)
    {
        string html = Render(template);

        html.Should().NotContainEquivalentOf("#7c3aed");
        Regex.Matches(html, "<h1 style=\"color:#19161D\">").Count.Should().Be(1, "every template has one ink heading");
        Regex.Matches(html, "background:#863BFF;color:#F5F1E8;").Count.Should().Be(expectedButtons);
        // No violet text anywhere: #863BFF appears only as a button background.
        Regex.Matches(html, "#863BFF", RegexOptions.IgnoreCase).Count.Should().Be(expectedButtons);
    }

    [Fact]
    public void StudioRegisteredAdmin_LinkIsUnderlinedInk_NotViolet()
    {
        // Admin-only email: its one link is text, so it is ink #19161D (17.89:1) and underlined, never violet text.
        string html = Renderer.RenderStudioRegisteredAdmin(
            "Ink Studio", "Tirana", "owner@example.test", null, new DateTime(2026, 12, 12, 0, 0, 0, DateTimeKind.Utc), false,
            "https://app.tattooos.co/admin/studios/x");

        html.Should().NotContainEquivalentOf("#7c3aed").And.NotContainEquivalentOf("#863BFF");
        html.Should().Contain("style=\"color:#19161D;font-weight:600;text-decoration:underline\"");
    }

    [Fact]
    public void AcrossTheTemplates_TenHeadingsAndEightButtonsMoved_MatchingTheAudit()
    {
        // The two guest-collision variants share one heading, so it is counted once.
        int headings = Templates.Where(row => (string)row[0] != "GuestBookingEmailCollisionWithResume")
            .Sum(row => Regex.Matches(Render((string)row[0]), "<h1 style=\"color:#19161D\">").Count);
        int buttons = Templates.Sum(row => Regex.Matches(Render((string)row[0]), "background:#863BFF;color:#F5F1E8;").Count);

        headings.Should().Be(10);
        buttons.Should().Be(8);
    }
}
