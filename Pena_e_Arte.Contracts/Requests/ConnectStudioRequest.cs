namespace Pena_e_Arte.Contracts.Requests;

public record ConnectStudioRequest(
    string ReturnUrl,
    string RefreshUrl,
    string Country);
