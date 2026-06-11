using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Clients.Commands;

public record CreateClientCommand(CreateClientRequest Request) : IRequest<ClientResponse>;

public class CreateClientHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<CreateClientCommand, ClientResponse>
{
    public async Task<ClientResponse> Handle(CreateClientCommand command, CancellationToken ct)
    {
        CreateClientRequest req = command.Request;

        bool exists = await db.Clients.AnyAsync(c => c.Email == req.Email, ct);
        if (exists)
            throw new BusinessRuleViolationException($"A client with email '{req.Email}' already exists in this studio.");

        Client client = new()
        {
            StudioId  = tenant.StudioId,
            FirstName = req.FirstName,
            LastName  = req.LastName,
            Email     = req.Email,
            Phone     = req.Phone
        };

        db.Clients.Add(client);
        await db.SaveChangesAsync(ct);

        return Map(client);
    }

    internal static ClientResponse Map(Client c) =>
        new(c.Id, c.StudioId, c.FirstName, c.LastName, c.Email, c.Phone, c.CreatedAt, c.UserId);
}
