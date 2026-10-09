using System.Text;
using Microsoft.Identity.Client;

namespace LogEase.Desktop;

public sealed class AzureAuthHelper
{
    private readonly AuthConfig _config;

    private static readonly string[] TokenScopes =
    [
        "User.Read",
    ];

    private static string AuthLogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LogEase",
            "azure-auth.log");

    public AzureAuthHelper(AuthConfig config) => _config = config;

    public async Task<(bool Ok, string? IdToken, string? AccessToken, string? Error)> SignInAsync(IWin32Window? parent = null)
    {
        if (!_config.AzureEnabled || string.IsNullOrWhiteSpace(_config.ClientId) || string.IsNullOrWhiteSpace(_config.TenantId))
            return (false, null, null, "Azure sign-in is not configured on the server.");

        AzureDeviceCodeForm? deviceUi = null;
        try
        {
            WriteLog($"Sign-in start. ClientId={_config.ClientId} TenantId={_config.TenantId}");

            var app = BuildApp();

            // Clear broken cache from earlier SPA attempts.
            foreach (var account in await app.GetAccountsAsync())
            {
                try { await app.RemoveAsync(account); } catch { /* ignore */ }
            }

            AuthenticationResult result;
            // Prefer device-code first: interactive http://localhost is registered as SPA in this tenant
            // and causes AADSTS9002327. Device-code already works for InfozoApp.
            try
            {
                WriteLog("Trying device-code (recommended for this Azure app)…");
                result = await app.AcquireTokenWithDeviceCode(TokenScopes, deviceCode =>
                {
                    WriteLog($"Device code issued. Url={deviceCode.VerificationUrl} Code={deviceCode.UserCode}");

                    void ShowUi()
                    {
                        var dlg = new AzureDeviceCodeForm(deviceCode.VerificationUrl, deviceCode.UserCode);
                        deviceUi = dlg;
                        if (parent is not null)
                            dlg.Show(parent);
                        else
                            dlg.Show();
                    }

                    if (parent is Control c && c.IsHandleCreated)
                    {
                        if (c.InvokeRequired)
                            c.Invoke(ShowUi);
                        else
                            ShowUi();
                    }
                    else
                    {
                        ShowUi();
                    }

                    return Task.CompletedTask;
                }).ExecuteAsync();
            }
            catch (Exception deviceEx)
            {
                WriteLog("Device-code failed:\n" + FormatFullError(deviceEx));
                WriteLog("Trying interactive with msal:// auth scheme…");

                var interactive = app
                    .AcquireTokenInteractive(TokenScopes)
                    .WithPrompt(Prompt.SelectAccount)
                    .WithUseEmbeddedWebView(false);

                if (parent is not null)
                    interactive = interactive.WithParentActivityOrWindow(parent);

                result = await interactive.ExecuteAsync();
            }

            if (string.IsNullOrWhiteSpace(result.IdToken))
            {
                var detail =
                    "Azure login returned no ID token.\n"
                    + $"Account={result.Account?.Username}\n"
                    + $"Scopes={string.Join(' ', result.Scopes ?? Array.Empty<string>())}\n"
                    + "Enable ID tokens in Entra → Authentication.";
                WriteLog(detail);
                return (false, null, null, detail);
            }

            WriteLog($"Sign-in OK. Account={result.Account?.Username} HasAccessToken={!string.IsNullOrWhiteSpace(result.AccessToken)}");
            return (true, result.IdToken, result.AccessToken, null);
        }
        catch (Exception ex)
        {
            var detail = FormatFullError(ex);
            WriteLog(detail);
            try { Clipboard.SetText(detail); } catch { /* ignore */ }
            return (false, null, null, detail);
        }
        finally
        {
            CloseDeviceUi(deviceUi);
        }
    }

    private IPublicClientApplication BuildApp()
    {
        var authority = $"{_config.Instance.TrimEnd('/')}/{_config.TenantId}";
        // Custom MSAL scheme — must be under Mobile and desktop (not SPA).
        // Do NOT use http://localhost here: this tenant already registered localhost as SPA.
        var redirect = $"msal{_config.ClientId}://auth";
        return PublicClientApplicationBuilder
            .Create(_config.ClientId)
            .WithAuthority(authority)
            .WithRedirectUri(redirect)
            .Build();
    }

    private static void CloseDeviceUi(AzureDeviceCodeForm? deviceUi)
    {
        if (deviceUi is null || deviceUi.IsDisposed) return;
        try
        {
            if (deviceUi.IsHandleCreated && deviceUi.InvokeRequired)
            {
                deviceUi.BeginInvoke(new Action(() =>
                {
                    try { if (!deviceUi.IsDisposed) deviceUi.Close(); } catch { /* ignore */ }
                }));
            }
            else
            {
                deviceUi.Close();
            }
        }
        catch { /* ignore */ }
    }

    public static string FormatFullError(Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Azure sign-in failed — full details:");
        sb.AppendLine();

        var depth = 0;
        for (Exception? e = ex; e is not null; e = e.InnerException, depth++)
        {
            sb.AppendLine($"[{depth}] {e.GetType().FullName}");
            if (e is MsalException msal)
            {
                sb.AppendLine($"    ErrorCode: {msal.ErrorCode}");
                sb.AppendLine($"    Message:   {msal.Message}");
                if (msal is MsalServiceException svc)
                {
                    sb.AppendLine($"    StatusCode: {svc.StatusCode}");
                    sb.AppendLine($"    CorrelationId: {svc.CorrelationId}");
                    if (!string.IsNullOrWhiteSpace(svc.ResponseBody))
                        sb.AppendLine($"    ResponseBody: {svc.ResponseBody}");
                }
            }
            else
            {
                sb.AppendLine($"    Message: {e.Message}");
            }

            if (!string.IsNullOrWhiteSpace(e.StackTrace))
            {
                sb.AppendLine("    Stack:");
                sb.AppendLine(e.StackTrace);
            }

            sb.AppendLine();
        }

        sb.AppendLine("Hints:");
        sb.AppendLine("- Entra → Authentication → Mobile and desktop applications → add: http://localhost");
        sb.AppendLine("- Entra → Authentication → Allow public client flows = Yes");
        sb.AppendLine("- Entra → Authentication → ID tokens enabled");
        sb.AppendLine("- Microsoft Graph User.Read (or User.ReadWrite) delegated + admin consent");
        sb.AppendLine();
        sb.AppendLine("Log file:");
        sb.AppendLine(AuthLogPath);
        sb.AppendLine();
        sb.AppendLine("(Details were copied to the clipboard if possible.)");
        return sb.ToString().TrimEnd();
    }

    private static void WriteLog(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AuthLogPath)!);
            File.AppendAllText(AuthLogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* ignore */ }
    }
}

public sealed class AuthConfig
{
    public bool AzureEnabled { get; set; }
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string Instance { get; set; } = "https://login.microsoftonline.com/";
    public List<string> Scopes { get; set; } = ["https://graph.microsoft.com/User.Read"];
}

public sealed class LoginResponse
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public string? Error { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? ProfilePhotoUrl { get; set; }
    public bool HasKimaiToken { get; set; }
}
