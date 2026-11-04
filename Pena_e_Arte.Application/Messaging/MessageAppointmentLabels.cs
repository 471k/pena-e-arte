using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Messaging;

/// <summary>
/// Resolves the "which appointment is this about" label for chat messages and inbox rows.
///
/// Only the appointment id is ever stored (on ChatMessage, and denormalized on Conversation for
/// the last message). The label — start time and service name — is read from the appointment
/// row here, on every request, so a reschedule or a service change shows straight away instead
/// of a stale cached date.
///
/// No authorization check of its own, on purpose: SendChatMessageHandler only stores an id whose
/// appointment is in this studio and belongs to the client participant of that conversation, so
/// anyone allowed to read the conversation may see the label. The tenant query filter still
/// applies; an id that no longer resolves (soft-deleted appointment) simply gets no label.
/// </summary>
public static class MessageAppointmentLabels
{
    /// <summary>All labels for the given ids in a single query, however many there are; no query
    /// at all when none of them is set. Ids that don't resolve are absent from the result.</summary>
    public static async Task<Dictionary<Guid, MessageAppointmentLabelResponse>> ResolveAsync(
        IAppDbContext db, IEnumerable<Guid?> appointmentIds, CancellationToken ct)
    {
        List<Guid> ids = [.. appointmentIds.OfType<Guid>().Distinct()];
        if (ids.Count == 0) return [];

        var rows = await db.Appointments
            .Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.Date, ServiceName = a.Service != null ? a.Service.Name : null })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.Id, r => Build(r.Id, r.Date, r.ServiceName));
    }

    /// <summary>The label for one appointment. A missing or blank service name becomes null, so
    /// the client shows the date and time alone rather than an empty segment.</summary>
    public static MessageAppointmentLabelResponse Build(Guid appointmentId, DateTime startsAt, string? serviceName) =>
        new(appointmentId, startsAt, string.IsNullOrWhiteSpace(serviceName) ? null : serviceName.Trim());

    /// <summary>The label for a stored id, or null when there is no id or it didn't resolve.</summary>
    public static MessageAppointmentLabelResponse? For(
        Dictionary<Guid, MessageAppointmentLabelResponse> labels, Guid? appointmentId) =>
        appointmentId is { } id && labels.TryGetValue(id, out MessageAppointmentLabelResponse? label) ? label : null;
}
