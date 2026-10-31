using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.Infrastructure.Services.MailKit;
using Pena_e_Arte.UnitTests.ConsentForms;
using Resend;

namespace Pena_e_Arte.UnitTests.Services;

public class NotificationServiceEmailTests
{
    private readonly IResend _resend = Substitute.For<IResend>();
    private readonly CapturingLogger<NotificationService> _logger = new();
    private EmailMessage? _sent;

    public NotificationServiceEmailTests()
    {
        _resend.EmailSendAsync(Arg.Do<EmailMessage>(m => _sent = m), Arg.Any<CancellationToken>())
            .Returns(new ResendResponse<Guid>(Guid.NewGuid(), null));
    }

    private NotificationService CreateSut() => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Resend:FromAddress"] = "notifications@example.test",
        }).Build(),
        _resend,
        _logger);

    [Fact]
    public async Task SendEmailAsync_SendsAPlainTextPartWithTheLinksKept()
    {
        string html = new EmailRenderer().RenderGuestBookingWelcome(
            "Ink & Iron", "https://app.example.test/reset-password?token=a&b=1", "https://app.example.test/verify-email?token=c");

        await CreateSut().SendEmailAsync("guest@example.test", "Subject", html);

        _sent!.HtmlBody.Should().Be(html);
        string text = _sent.TextBody!;
        text.Should().StartWith("Your booking request was sent!");
        text.Should().Contain("Ink & Iron has received your booking request");
        text.Should().Contain("Set your password (https://app.example.test/reset-password?token=a&b=1)");
        text.Should().Contain("Confirm email (https://app.example.test/verify-email?token=c)");
        text.Should().NotContain("<").And.NotContain("&amp;");
        // The <title> is not repeated as body text.
        text.Should().NotContain("Your booking at Ink & Iron");
    }

    [Fact]
    public async Task SendEmailAsync_NeverLogsTheSubject()
    {
        // Subjects can name the client (CLAUDE.md rule 3: never log PII).
        await CreateSut().SendEmailAsync("owner@example.test", "New booking request from Jamie Guest", "<p>Hi</p>");

        _logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Information);
        _logger.Entries.Should().NotContain(e => e.Message.Contains("Jamie"));
    }

    [Fact]
    public void FromHtml_KeepsParagraphsApartAndDrawsRules()
    {
        string text = EmailPlainText.FromHtml(
            "<html><head><style>p{color:red}</style></head><body><h2>Cancelled</h2><p>Line one<br/>line two</p><hr/><p>TattooOS &mdash; Studio</p></body></html>");

        text.Should().Be("Cancelled\n\nLine one\nline two\n\n----\nTattooOS — Studio");
    }
}
