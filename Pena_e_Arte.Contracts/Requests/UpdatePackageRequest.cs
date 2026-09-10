namespace Pena_e_Arte.Contracts.Requests;

public record UpdatePackageRequest(string Name, int SessionCount, decimal Price, bool IsActive);
