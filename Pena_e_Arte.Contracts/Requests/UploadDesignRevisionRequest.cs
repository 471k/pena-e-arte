namespace Pena_e_Arte.Contracts.Requests;

public record UploadDesignRevisionRequest(
    Guid    DesignId,
    string  FileUrl,
    string? Notes);
