using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Pena_e_Arte.API.Middleware;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.ConsentForms;

namespace Pena_e_Arte.UnitTests.Middleware;

/// <summary>
/// C03 (slice 7): a slot taken between the list and the submit reaches the booking form as 409 with a code, so the
/// form can keep the client's details and offer alternatives without matching on the message text.
/// </summary>
public class ExceptionMiddlewareSlotTakenTests
{
    [Fact]
    public async Task SlotAlreadyBooked_Is409_WithItsCode()
    {
        DefaultHttpContext context = new();
        context.Response.Body = new MemoryStream();

        await new ExceptionMiddleware(_ => throw new SlotAlreadyBookedException(), new CapturingLogger<ExceptionMiddleware>())
            .InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        context.Response.Body.Position = 0;
        using JsonDocument body = await JsonDocument.ParseAsync(context.Response.Body);
        body.RootElement.GetProperty("code").GetString().Should().Be("SLOT_ALREADY_BOOKED");
        body.RootElement.GetProperty("message").GetString().Should().Be("The selected time slot is no longer available.");
    }
}
