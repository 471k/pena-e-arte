using MediatR;

namespace Pena_e_Arte.Application.Billing.Commands.CreateBillingPortal;

public sealed record CreateBillingPortalCommand(string ReturnUrl) : IRequest<CreateBillingPortalResult>;

public sealed record CreateBillingPortalResult(string Url);
