using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ConsentForms.Commands;

public record SignConsentFormCommand(SignConsentFormRequest Request) : IRequest<ConsentFormResponse>;

public class SignConsentFormHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<SignConsentFormCommand, ConsentFormResponse>
{
    public async Task<ConsentFormResponse> Handle(SignConsentFormCommand command, CancellationToken ct)
    {
        SignConsentFormRequest req = command.Request;

        bool alreadySigned = await db.ConsentForms
            .AnyAsync(c => c.AppointmentId == req.AppointmentId, ct);

        if (alreadySigned) throw new ConsentFormAlreadySignedException();

        ConsentForm form = new()
        {
            StudioId      = tenant.StudioId,
            ClientId      = req.ClientId,
            AppointmentId = req.AppointmentId,
            SignatureData = req.SignatureData,
            FileUrl       = req.FileUrl,
            SignedAt      = DateTime.UtcNow
        };

        db.ConsentForms.Add(form);
        await db.SaveChangesAsync(ct);

        return Map(form);
    }

    internal static ConsentFormResponse Map(ConsentForm f) =>
        new(f.Id, f.StudioId, f.ClientId, f.AppointmentId, f.FileUrl, f.SignatureData, f.SignedAt, f.CreatedAt);
}
