namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// The active consent template for the caller's studio, shown in full before signing.
/// Null body when no template is configured (older studios) — the UI falls back to the
/// generic procedural copy in that case.
/// </summary>
public record ConsentTemplateResponse(
    Guid? Id,
    string Kind,
    string Version,
    string BodyText);
