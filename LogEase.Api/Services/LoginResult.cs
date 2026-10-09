using LogEase.Api.Entities;

namespace LogEase.Api.Services;

public sealed class LoginResult
{
    public bool Ok { get; init; }
    public string? Token { get; init; }
    public string? Error { get; init; }
    public Guid UserId { get; init; }
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
    public string? ProfilePhotoUrl { get; init; }
    public bool HasKimaiToken { get; init; }

    public static LoginResult Fail(string error) =>
        new() { Ok = false, Error = error };

    public static LoginResult Success(string token, User user) =>
        new()
        {
            Ok = true,
            Token = token,
            UserId = user.Id,
            Name = user.Name,
            Email = user.Email,
            ProfilePhotoUrl = user.ProfilePhotoUrl,
            HasKimaiToken = !string.IsNullOrWhiteSpace(user.KimaiApiToken),
        };
}
