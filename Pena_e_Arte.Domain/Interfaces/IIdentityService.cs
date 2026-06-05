namespace Pena_e_Arte.Domain.Interfaces;

public interface IIdentityService
{
    Task<(bool Success, string[] Errors)> CreateUserAsync(string email, string password, string role, Guid studioId);
    Task<(bool Success, string? Token, string? Error)> LoginAsync(string email, string password);
    Task<(bool Success, string? Token, string? Error)> GeneratePasswordResetTokenAsync(string email);
    Task<(bool Success, string[] Errors)> ResetPasswordAsync(string email, string token, string newPassword);
}
