using MediatR;
using Pena_e_Arte.Application.Messaging.Commands;
using Pena_e_Arte.Application.Messaging.Queries;
using Pena_e_Arte.Contracts.Requests;

namespace Pena_e_Arte.API.Endpoints;

public static class MessagingEndpoints
{
    public static void MapMessagingEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/conversations")
            .RequireAuthorization("ClientAndAbove");

        group.MapGet("", GetConversations);
        group.MapGet("contacts", GetContacts);
        group.MapGet("unread-count", GetUnreadCount);
        group.MapPost("", CreateConversation);
        group.MapGet("{id:guid}/messages", GetMessages);
        group.MapPost("{id:guid}/messages", SendMessage);
        group.MapPost("{id:guid}/read", MarkRead);
    }

    private static async Task<IResult> GetConversations(ISender mediator, CancellationToken ct) =>
        Results.Ok(await mediator.Send(new GetConversationsQuery(), ct));

    private static async Task<IResult> GetContacts(ISender mediator, CancellationToken ct) =>
        Results.Ok(await mediator.Send(new GetConversationContactsQuery(), ct));

    private static async Task<IResult> GetUnreadCount(ISender mediator, CancellationToken ct) =>
        Results.Ok(await mediator.Send(new GetUnreadMessageCountQuery(), ct));

    // Deliberate deviation from the "201 for a creating POST" convention (conventions.md):
    // this is a get-or-create endpoint — the caller (a "message this person" button) never
    // knows in advance whether a thread already exists, so it always returns 200 whether it
    // found an existing conversation or created a new one. See messaging Decision 9.
    private static async Task<IResult> CreateConversation(
        CreateConversationRequest request, ISender mediator, CancellationToken ct) =>
        Results.Ok(await mediator.Send(new CreateConversationCommand(request), ct));

    private static async Task<IResult> GetMessages(
        Guid id, ISender mediator, CancellationToken ct, Guid? before = null, int take = 30) =>
        Results.Ok(await mediator.Send(new GetConversationMessagesQuery(id, before, take), ct));

    private static async Task<IResult> SendMessage(
        Guid id, SendChatMessageRequest request, ISender mediator, CancellationToken ct) =>
        Results.Created($"/api/v1/conversations/{id}/messages",
            await mediator.Send(new SendChatMessageCommand(id, request), ct));

    private static async Task<IResult> MarkRead(Guid id, ISender mediator, CancellationToken ct)
    {
        await mediator.Send(new MarkConversationReadCommand(id), ct);
        return Results.NoContent();
    }
}
