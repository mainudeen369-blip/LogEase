using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LogEase.Api.Data;
using LogEase.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace LogEase.Api.Services;

public class AuthService
{
    private readonly LogEaseDbContext _db;
    private readonly IConfiguration _config;

    public AuthService(LogEaseDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<LoginResult> LoginAsync(string email, string password, CancellationToken ct)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalized, ct);
        if (user is null || string.IsNullOrWhiteSpace(user.PasswordHash)
            || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            return LoginResult.Fail("Invalid email or password.");
        }

        var token = CreateToken(user.Id, user.Email, user.Name);
        return LoginResult.Success(token, user);
    }

    public string CreateToken(Guid userId, string email, string name)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddHours(int.Parse(_config["Jwt:ExpireHours"] ?? "12"));

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(ClaimTypes.Name, name),
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
