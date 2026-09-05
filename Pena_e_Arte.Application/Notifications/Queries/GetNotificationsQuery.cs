using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Notifications.Queries;

public record GetNotificationsQuery(
    Guid? RecipientId,
    string? Channel,
    DateTime? From,
    DateTime? To) : IRequest<List<NotificationLogResponse>>;

public class GetNotificationsHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetNotificationsQuery, List<NotificationLogResponse>>
{
    public async Task<List<NotificationLogResponse>> Handle(
        GetNotificationsQuery query, CancellationToken ct)
    {
        IQueryable<NotificationLog> q = db.NotificationLogs.AsNoTracking();

        if (currentUser.Role == "artist")
        {
            // An artist only ever sees notifications addressed to them — never the
            // full studio log (which may include other artists' or clients' details).
            Guid? myArtistId = await db.Artists
                .Where(a => a.UserId == currentUser.UserId)
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(ct);

            q = q.Where(n => n.RecipientType == NotificationRecipientType.Artist
                           && n.RecipientId == myArtistId);
        }
        else if (currentUser.Role == "client")
        {
            // A client only ever sees notifications addressed to them — any
            // requested RecipientId is ignored rather than trusted, since another
            // client's or the studio's own id could otherwise be guessed.
            Client? me = await db.FindClientForUserAsync(currentUser, ct);

            q = q.Where(n => n.RecipientType == NotificationRecipientType.Client
                           && n.RecipientId == (me == null ? Guid.Empty : me.Id));
        }
        else if (query.RecipientId.HasValue)
        {
            q = q.Where(n => n.RecipientId == query.RecipientId.Value);
        }

        if (query.Channel is not null)
            q = q.Where(n => n.Channel.ToString() == query.Channel);

        if (query.From.HasValue)
            q = q.Where(n => n.SentAt >= query.From.Value);

        if (query.To.HasValue)
            q = q.Where(n => n.SentAt <= query.To.Value);

        List<NotificationLog> logs = await q
            .OrderByDescending(n => n.SentAt)
            .ToListAsync(ct);

        Dictionary<Guid, string> names = await ResolveRecipientNamesAsync(logs, ct);

        return logs
            .Select(n => Map(n, n.RecipientId.HasValue ? names.GetValueOrDefault(n.RecipientId.Value) : null))
            .ToList();
    }

    // RecipientId is polymorphic (a Client.Id or a Studio.Id, per RecipientType) — batch-resolve
    // both in two queries rather than N+1-ing per row. A missing lookup (e.g. a deleted client)
    // leaves the name out of the dictionary; callers fall back to showing the raw id.
    private async Task<Dictionary<Guid, string>> ResolveRecipientNamesAsync(
        List<NotificationLog> logs, CancellationToken ct)
    {
        List<Guid> clientIds = logs
            .Where(n => n.RecipientType == NotificationRecipientType.Client && n.RecipientId.HasValue)
            .Select(n => n.RecipientId!.Value)
            .Distinct()
            .ToList();

        List<Guid> studioIds = logs
            .Where(n => n.RecipientType == NotificationRecipientType.Studio && n.RecipientId.HasValue)
            .Select(n => n.RecipientId!.Value)
            .Distinct()
            .ToList();

        List<Guid> artistIds = logs
            .Where(n => n.RecipientType == NotificationRecipientType.Artist && n.RecipientId.HasValue)
            .Select(n => n.RecipientId!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, string> names = [];

        if (clientIds.Count > 0)
        {
            List<(Guid Id, string Name)> clients = await db.Clients
                .AsNoTracking()
                .Where(c => clientIds.Contains(c.Id))
                .Select(c => new ValueTuple<Guid, string>(c.Id, c.FirstName + " " + c.LastName))
                .ToListAsync(ct);

            foreach ((Guid id, string name) in clients)
                names[id] = name;
        }

        if (studioIds.Count > 0)
        {
            List<(Guid Id, string Name)> studios = await db.Studios
                .AsNoTracking()
                .Where(s => studioIds.Contains(s.Id))
                .Select(s => new ValueTuple<Guid, string>(s.Id, s.Name))
                .ToListAsync(ct);

            foreach ((Guid id, string name) in studios)
                names[id] = name;
        }

        if (artistIds.Count > 0)
        {
            List<(Guid Id, string Name)> artists = await db.Artists
                .AsNoTracking()
                .Where(a => artistIds.Contains(a.Id))
                .Select(a => new ValueTuple<Guid, string>(a.Id, a.FirstName + " " + a.LastName))
                .ToListAsync(ct);

            foreach ((Guid id, string name) in artists)
                names[id] = name;
        }

        return names;
    }

    internal static NotificationLogResponse Map(NotificationLog n, string? recipientName = null) => new(
        n.Id, n.RecipientId, recipientName, n.Channel.ToString(),
        n.Subject, n.Body, n.SentAt, n.IsSuccess, n.CreatedAt);
}

public class GetNotificationsValidator : AbstractValidator<GetNotificationsQuery>
{
    public GetNotificationsValidator()
    {
        RuleFor(x => x.Channel)
            .Must(c => c is null or "Email" or "Sms" or "InApp")
            .WithMessage("Channel must be 'Email', 'Sms', or 'InApp'.");

        RuleFor(x => x)
            .Must(x => x.From is null || x.To is null || x.From <= x.To)
            .WithMessage("'from' must be before or equal to 'to'.")
            .OverridePropertyName("From");
    }
}
