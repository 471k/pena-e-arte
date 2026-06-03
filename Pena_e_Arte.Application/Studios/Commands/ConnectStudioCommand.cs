using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Studios.Commands;

public record ConnectStudioCommand(ConnectStudioRequest Request) : IRequest<ConnectOnboardingResponse>;

public class ConnectStudioHandler(
    IAppDbContext         db,
    ICurrentTenant        tenant,
    IStripeConnectService stripeConnect)
    : IRequestHandler<ConnectStudioCommand, ConnectOnboardingResponse>
{
    public async Task<ConnectOnboardingResponse> Handle(ConnectStudioCommand command, CancellationToken ct)
    {
        Studio studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == tenant.StudioId, ct)
            ?? throw new NotFoundException(nameof(Studio), tenant.StudioId);

        if (studio.StripeAccountId is null)
        {
            string accountId = await stripeConnect.CreateConnectedAccountAsync(
                studio.OwnerEmail, command.Request.Country, ct);

            studio.StripeAccountId = accountId;
            await db.SaveChangesAsync(ct);
        }

        string url = await stripeConnect.CreateAccountLinkAsync(
            studio.StripeAccountId,
            command.Request.ReturnUrl,
            command.Request.RefreshUrl,
            ct);

        return new ConnectOnboardingResponse(url);
    }
}
