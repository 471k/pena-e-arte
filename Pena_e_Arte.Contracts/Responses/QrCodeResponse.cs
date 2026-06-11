namespace Pena_e_Arte.Contracts.Responses;

public record QrCodeResponse(byte[] Data, string ContentType, string Slug);
