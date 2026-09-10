namespace Pena_e_Arte.Contracts.Responses;

public record IntakeFormTemplateResponse(
    Guid Id,
    Guid StudioId,
    string FieldSchemaJson,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);
