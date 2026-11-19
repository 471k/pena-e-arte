using MediatR;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Platform.Queries;

/// <summary>The named action and target-type choices for the admin audit log's filters (D01).</summary>
public record GetAuditLogVocabularyQuery : IRequest<AuditLogVocabularyResponse>;

public class GetAuditLogVocabularyHandler : IRequestHandler<GetAuditLogVocabularyQuery, AuditLogVocabularyResponse>
{
    public Task<AuditLogVocabularyResponse> Handle(GetAuditLogVocabularyQuery query, CancellationToken ct) =>
        Task.FromResult(new AuditLogVocabularyResponse(AuditVocabulary.Actions, AuditVocabulary.TargetTypes));
}
