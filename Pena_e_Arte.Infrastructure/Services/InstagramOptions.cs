namespace Pena_e_Arte.Infrastructure.Services;

public class InstagramOptions
{
    public const string Section = "Instagram";

    public string AppId              { get; init; } = "";
    public string AppSecret          { get; init; } = "";
    public string RedirectUri        { get; init; } = "";
    public string TokenEncryptionKey { get; init; } = "";
}
