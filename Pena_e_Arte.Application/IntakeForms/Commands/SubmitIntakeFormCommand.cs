using MediatR;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.IntakeForms.Commands;

public record SubmitIntakeFormCommand(SubmitIntakeFormRequest Request) : IRequest<IntakeFormResponse>;

public class SubmitIntakeFormHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<SubmitIntakeFormCommand, IntakeFormResponse>
{
    public async Task<IntakeFormResponse> Handle(SubmitIntakeFormCommand command, CancellationToken ct)
    {
        SubmitIntakeFormRequest req = command.Request;

        IntakeForm form = new()
        {
            StudioId      = tenant.StudioId,
            ClientId      = req.ClientId,
            AppointmentId = req.AppointmentId,
            FormData      = req.FormData,
            FileUrl       = req.FileUrl,
            SubmittedAt   = DateTime.UtcNow
        };

        db.IntakeForms.Add(form);
        await db.SaveChangesAsync(ct);

        return Map(form);
    }

    internal static IntakeFormResponse Map(IntakeForm f) =>
        new(f.Id, f.StudioId, f.ClientId, f.AppointmentId, f.FormData, f.FileUrl, f.SubmittedAt, f.CreatedAt);
}
