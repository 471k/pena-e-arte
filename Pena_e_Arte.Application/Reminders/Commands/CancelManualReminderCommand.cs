using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Reminders.Commands;

public record CancelManualReminderCommand(Guid Id) : IRequest, IAuditableCommand
{
    public string AuditAction => AuditActions.ManualReminderCancelled;
    public string AuditTargetType => AuditTargetTypes.ManualReminder;
    public Guid AuditTargetId => Id;
}

public class CancelManualReminderHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IJobScheduler jobs)
    : IRequestHandler<CancelManualReminderCommand>
{
    public async Task Handle(CancelManualReminderCommand command, CancellationToken ct)
    {
        ManualReminder reminder = await db.ManualReminders
            .Include(m => m.Artist)
            .FirstOrDefaultAsync(m => m.Id == command.Id, ct)
            ?? throw new NotFoundException(nameof(ManualReminder), command.Id);

        // Artist is nullable at runtime despite its non-nullable-looking `= null!` default —
        // the global query filter excludes a soft-deleted artist, so .Include(m => m.Artist)
        // silently yields null once the linked artist is deleted. Treated the same as any
        // other ownership mismatch: a caller can't prove this is theirs, so it isn't theirs.
        if (currentUser.Role == "artist" && (reminder.Artist is null || reminder.Artist.UserId != currentUser.UserId))
            throw new NotFoundException(nameof(ManualReminder), command.Id);

        if (reminder.Status != ManualReminderStatus.Scheduled)
            throw new ConflictException(
                $"This reminder is already {reminder.Status.ToString().ToLowerInvariant()} and can no longer be cancelled.");

        if (!string.IsNullOrEmpty(reminder.JobId))
            jobs.CancelJob(reminder.JobId);

        // Re-check directly against the database (bypassing this DbContext's identity map via
        // AsNoTracking, so a concurrent request's already-committed write is actually visible)
        // right before writing — narrows the window where ManualReminderJob could concurrently
        // transition this same reminder to Sent/Failed between the read above and this write.
        bool stillScheduled = await db.ManualReminders
            .AsNoTracking()
            .AnyAsync(m => m.Id == reminder.Id && m.Status == ManualReminderStatus.Scheduled, ct);

        if (!stillScheduled)
            throw new ConflictException(
                "This reminder has already been sent and can no longer be cancelled.");

        reminder.Status = ManualReminderStatus.Cancelled;
        reminder.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
