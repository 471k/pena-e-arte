using FluentAssertions;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Infrastructure.Services.MailKit;

namespace Pena_e_Arte.UnitTests.Appointments;

/// <summary>
/// DEC11: the aftercare email and the client appointment page show the same advice because both read
/// AftercareInstructions.Steps; there is no second copy of the text.
/// </summary>
public class AftercareContentTests
{
    [Fact]
    public void TheAftercareEmail_RendersEveryStep_FromTheSharedList()
    {
        string html = new EmailRenderer().RenderAftercare("Marco", "Ink Studio", "Ari Kola", showBranding: false);

        AftercareInstructions.Steps.Should().NotBeEmpty();
        foreach (string step in AftercareInstructions.Steps)
            html.Should().Contain($"<li>{System.Net.WebUtility.HtmlEncode(step)}</li>");
        html.Split("<li>").Length.Should().Be(AftercareInstructions.Steps.Count + 1);
    }
}
