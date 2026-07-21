using MediatR;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Help.Commands;

public record LogHelpSearchCommand(LogHelpSearchRequest Request) : IRequest;

public class LogHelpSearchHandler(
    IAppDbContext  db,
    ICurrentTenant tenant,
    ICurrentUser   user)
    : IRequestHandler<LogHelpSearchCommand>
{
    public async Task Handle(LogHelpSearchCommand command, CancellationToken ct)
    {
        HelpSearchLog log = HelpSearchLog.Create(
            studioId:    tenant.StudioId,
            userId:      user.UserId,
            role:        user.Role,
            query:       command.Request.Query,
            resultCount: command.Request.ResultCount);

        db.HelpSearchLogs.Add(log);
        await db.SaveChangesAsync(ct);
    }
}
