using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using LogEase.Api.Data;
using LogEase.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace LogEase.Api.Services;

public sealed class AzureAdAuthService
{
    private readonly LogEaseDbContext _db;
    private readonly AuthService _auth;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AzureAdAuthService> _log;
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _oidcConfig;

    public AzureAdAuthService(
        LogEaseDbContext db,
        AuthService auth,
        IConfiguration config,
        IHttpClientFactory httpFactory,
        ILogger<AzureAdAuthService> log)
    {
        _db = db;
        _auth = auth;
        _config = config;
        _httpFactory = httpFactory;
        _log = log;

        if (IsConfigured)
        {
            var authority = Authority.TrimEnd('/') + "/v2.0";
            _oidcConfig = new ConfigurationManager<OpenIdConnectConfiguration>(
                authority + "/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever());
        }
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_config["AzureAd:TenantId"])
        && !string.IsNullOrWhiteSpace(_config["AzureAd:ClientId"]);

    private string Authority
    {
        get
        {
            var instance = (_config["AzureAd:Instance"] ?? "https://login.microsoftonline.com/").TrimEnd('/');
            var tenantId = _config["AzureAd:TenantId"]!.Trim();
            return $"{instance}/{tenantId}";
        }
    }

    public async Task<LoginResult> LoginWithIdTokenAsync(
        string idToken,
        string? accessToken,
        CancellationToken ct)
    {
        try
        {
            if (!IsConfigured)
                return LoginResult.Fail("Azure AD is not configured on the server.");

            if (string.IsNullOrWhiteSpace(idToken))
                return LoginResult.Fail("Azure ID token is required.");

            ClaimsPrincipal principal;
            try
            {
                principal = await ValidateIdTokenAsync(idToken, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Azure ID token validation failed");
                return LoginResult.Fail("Azure sign-in could not be verified: " + ex.Message);
            }

            var oid = FirstClaim(principal,
                "oid",
                "http://schemas.microsoft.com/identity/claims/objectidentifier",
                ClaimTypes.NameIdentifier);
            var email = FirstClaim(principal,
                "preferred_username",
                ClaimTypes.Upn,
                ClaimTypes.Email,
                "email",
                "upn",
                "unique_name");
            var name = FirstClaim(principal, "name", ClaimTypes.Name, ClaimTypes.GivenName)
                       ?? (email?.Contains('@') == true ? email.Split('@')[0] : "User");

            // Azure AD ID tokens almost never include "picture". Prefer Graph /me/photo/$value.
            var photo = await TryFetchProfilePhotoDataUrlAsync(accessToken, ct)
                        ?? FirstClaim(principal, "picture");

            if (string.IsNullOrWhiteSpace(oid))
                return LoginResult.Fail("Azure token is missing the user id (oid).");
            if (string.IsNullOrWhiteSpace(email))
                email = $"{oid}@azuread.local";

            // Match DB column limits in LogEaseDbContext.
            email = Truncate(email.Trim(), 200);
            name = Truncate(name.Trim(), 120);
            // Photo may be a data: URL (large); only truncate short http(s) claim URLs.
            if (!string.IsNullOrWhiteSpace(photo)
                && !photo.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                && photo.Length > 2048)
            {
                photo = Truncate(photo.Trim(), 2048);
            }

            oid = Truncate(oid.Trim(), 64);

            var normalizedEmail = email.ToLowerInvariant();
            User? user;
            try
            {
                user = await _db.Users.FirstOrDefaultAsync(u => u.AzureObjectId == oid, ct);
                if (user is null)
                {
                    user = await _db.Users.FirstOrDefaultAsync(
                        u => u.Email.ToLower() == normalizedEmail, ct);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed querying users for Azure login (AzureObjectId column missing?)");
                return LoginResult.Fail(
                    "Database is missing Azure login columns. Restart LogEase.Api so it can add AzureObjectId / ProfilePhotoUrl. "
                    + "Detail: " + ex.Message);
            }

            if (user is null)
            {
                user = new User
                {
                    Id = Guid.NewGuid(),
                    Email = email,
                    Name = name,
                    AzureObjectId = oid,
                    ProfilePhotoUrl = photo,
                    PasswordHash = "",
                    CreatedAt = DateTime.UtcNow,
                };
                _db.Users.Add(user);
            }
            else
            {
                user.AzureObjectId ??= oid;
                user.Name = name;
                user.Email = email;
                if (!string.IsNullOrWhiteSpace(photo))
                    user.ProfilePhotoUrl = photo;
            }

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed saving Azure user {Email}", email);
                return LoginResult.Fail("Could not save Azure user to database: " + ex.Message);
            }

            var token = _auth.CreateToken(user.Id, user.Email, user.Name);
            return LoginResult.Success(token, user);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unexpected Azure login failure");
            return LoginResult.Fail("Azure login failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Loads the signed-in user's profile photo via Microsoft Graph.
    /// Returns a data URL suitable for &lt;img src&gt; / WinForms PictureBox.
    /// </summary>
    private async Task<string?> TryFetchProfilePhotoDataUrlAsync(string? accessToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return null;

        try
        {
            var client = _httpFactory.CreateClient();
            using var req = new HttpRequestMessage(
                HttpMethod.Get,
                "https://graph.microsoft.com/v1.0/me/photo/$value");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            if (!res.IsSuccessStatusCode)
            {
                _log.LogDebug(
                    "Graph profile photo returned {StatusCode}",
                    (int)res.StatusCode);
                return null;
            }

            var bytes = await res.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0 || bytes.Length > 2_000_000)
                return null;

            var contentType = res.Content.Headers.ContentType?.MediaType;
            if (string.IsNullOrWhiteSpace(contentType)
                || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                contentType = "image/jpeg";
            }

            return $"data:{contentType};base64,{Convert.ToBase64String(bytes)}";
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to fetch Azure profile photo from Microsoft Graph");
            return null;
        }
    }

    private async Task<ClaimsPrincipal> ValidateIdTokenAsync(string idToken, CancellationToken ct)
    {
        if (_oidcConfig is null)
            throw new InvalidOperationException("Azure AD is not configured.");

        var tenantId = _config["AzureAd:TenantId"]!.Trim();
        var clientId = _config["AzureAd:ClientId"]!.Trim();
        var oidc = await _oidcConfig.GetConfigurationAsync(ct);

        var validation = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers =
            [
                $"https://login.microsoftonline.com/{tenantId}/v2.0",
                $"https://sts.windows.net/{tenantId}/",
            ],
            ValidateAudience = true,
            ValidAudiences = [clientId, $"api://{clientId}"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = oidc.SigningKeys,
            ClockSkew = TimeSpan.FromMinutes(2),
            NameClaimType = "name",
            RoleClaimType = "roles",
        };

        var handler = new JwtSecurityTokenHandler
        {
            // Keep claim types as Azure sends them (oid, preferred_username, …).
            MapInboundClaims = false,
        };
        return handler.ValidateToken(idToken, validation, out _);
    }

    private static string? FirstClaim(ClaimsPrincipal principal, params string[] types)
    {
        foreach (var type in types)
        {
            var value = principal.FindFirst(type)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
