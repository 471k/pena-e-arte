namespace Pena_e_Arte.Contracts.Requests;

public record CreatePackageRequest(string Name, int SessionCount, decimal Price, bool IsActive);
