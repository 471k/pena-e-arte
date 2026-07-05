namespace Pena_e_Arte.Contracts.Responses;

/// <param name="IsLeavingActiveTenant">
/// True when the studio the user is leaving is currently their active (JWT-scoped) studio.
/// The client must log the user out and redirect to /discover in this case, since
/// their current token is now invalid for any tenant.
/// </param>
public record LeaveStudioResponse(bool IsLeavingActiveTenant);
