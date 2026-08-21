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

        if (currentUser.Role == "artist" && reminder.Artist.UserId != currentUser.UserId)
            throw new NotFoundException(nameof(ManualReminder), command.Id);

        if (reminder.Status != ManualReminderStatus.Scheduled)
            throw new ConflictException(
                $"This reminder is already {reminder.Status.ToString().ToLowerInvariant()} and can no longer be cancelled.");

        if (!string.IsNullOrEmpty(reminder.JobId))
            jobs.CancelJob(reminder.JobId);

        reminder.Status = ManualReminderStatus.Cancelled;
        reminder.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
