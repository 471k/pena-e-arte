using MediatR;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;

namespace Pena_e_Arte.Application.DepositRules.Queries;

/// <summary>
/// The platform defaults behind client self-service cancel/reschedule (no tenant data). The
/// owner's deposit-rule screens and the client's My bookings show these instead of a frontend copy.
/// </summary>
public record GetDepositRuleDefaultsQuery : IRequest<DepositRuleDefaultsResponse>;

public class GetDepositRuleDefaultsHandler : IRequestHandler<GetDepositRuleDefaultsQuery, DepositRuleDefaultsResponse>
{
    public Task<DepositRuleDefaultsResponse> Handle(GetDepositRuleDefaultsQuery query, CancellationToken ct) =>
        Task.FromResult(new DepositRuleDefaultsResponse(
            AppointmentSelfServiceDefaults.CancellationWindowHours,
            AppointmentSelfServiceDefaults.RefundPercentOnLateCancel));
}
