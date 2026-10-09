using LogEase.Api.Entities;
using LogEase.Api.Integrations.Gitlab;
using LogEase.Api.Integrations.Kimai;

namespace LogEase.Api.GraphQL;

internal static class UserSettingsMapper
{
    public static async Task<UserSettingsDto> ToDtoAsync(
        User u,
        KimaiTimesheetService kimai,
        GitlabActivityClient gitlab,
        CancellationToken ct)
    {
        KimaiUserDto? kimaiUser = null;
        string? kimaiError = null;
        var hasKimaiToken = !string.IsNullOrWhiteSpace(u.KimaiApiToken);

        if (hasKimaiToken && kimai.IsConfigured)
        {
            try
            {
                var profile = await kimai.GetCurrentUserAsync(u.KimaiApiToken!, ct);
                if (profile is not null)
                {
                    kimaiUser = new KimaiUserDto(
                        profile.Id,
                        profile.Username,
                        profile.Alias,
                        profile.Email,
                        profile.Title,
                        profile.DisplayName);
                }
                else
                {
                    kimaiError = "Could not load Kimai user profile.";
                }
            }
            catch (Exception ex)
            {
                kimaiError = ex.Message;
            }
        }
        else if (hasKimaiToken && !kimai.IsConfigured)
        {
            kimaiError = "Kimai:BaseUrl is not configured on the server.";
        }

        string? gitlabConnectedUser = null;
        string? gitlabError = null;
        var hasGitlabToken = !string.IsNullOrWhiteSpace(u.GitlabPersonalAccessToken);

        if (hasGitlabToken)
        {
            try
            {
                gitlabConnectedUser = await gitlab.ValidateTokenAsync(u, ct);
                if (gitlabConnectedUser is null)
                    gitlabError = "GitLab token did not return a user profile.";
            }
            catch (Exception ex)
            {
                gitlabError = ex.Message;
            }
        }

        return new UserSettingsDto(
            u.Id,
            u.Email,
            u.Name,
            u.ReminderEnabled,
            u.ReminderTime.ToString("HH:mm"),
            u.TimeZoneId,
            hasKimaiToken,
            kimaiUser,
            kimaiError,
            string.IsNullOrWhiteSpace(u.AiParseInstructions)
                ? null
                : u.AiParseInstructions,
            u.ProfilePhotoUrl,
            hasGitlabToken,
            string.IsNullOrWhiteSpace(u.GitlabBaseUrl) ? null : u.GitlabBaseUrl,
            string.IsNullOrWhiteSpace(u.GitlabUsername) ? null : u.GitlabUsername,
            gitlabConnectedUser,
            gitlabError,
            !string.IsNullOrWhiteSpace(u.MailImapHost)
                && !string.IsNullOrWhiteSpace(u.MailImapUser)
                && !string.IsNullOrWhiteSpace(u.MailImapPassword),
            string.IsNullOrWhiteSpace(u.MailImapHost) ? null : u.MailImapHost,
            u.MailImapPort,
            string.IsNullOrWhiteSpace(u.MailImapUser) ? null : u.MailImapUser,
            u.MailImapUseSsl);
    }
}
