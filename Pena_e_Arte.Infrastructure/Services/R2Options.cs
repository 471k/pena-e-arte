namespace Pena_e_Arte.Infrastructure.Services;

public class R2Options
{
    public const string Section = "CloudflareR2";

    public string AccountId       { get; init; } = string.Empty;
    public string AccessKeyId     { get; init; } = string.Empty;
    public string SecretAccessKey { get; init; } = string.Empty;
    public string BucketName      { get; init; } = string.Empty;
    public string PublicUrl       { get; init; } = string.Empty;
}
