using LogEase.Api.Entities;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;

namespace LogEase.Api.Integrations.Mail;

public sealed class MailActivityClient
{
    public async Task<string> FetchMailDigestAsync(
        User user,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(user.MailImapHost)
            || string.IsNullOrWhiteSpace(user.MailImapUser)
            || string.IsNullOrWhiteSpace(user.MailImapPassword))
            return "";

        var host = user.MailImapHost.Trim();
        var port = user.MailImapPort is > 0 ? user.MailImapPort.Value : (user.MailImapUseSsl ? 993 : 143);
        var useSsl = user.MailImapUseSsl;

        using var client = new ImapClient();
        try
        {
            await client.ConnectAsync(
                host,
                port,
                useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable,
                ct);
            await client.AuthenticateAsync(user.MailImapUser.Trim(), user.MailImapPassword, ct);
            await client.Inbox.OpenAsync(FolderAccess.ReadOnly, ct);

            var query = SearchQuery.DeliveredAfter(from.ToDateTime(TimeOnly.MinValue).AddDays(-1))
                .And(SearchQuery.DeliveredBefore(to.ToDateTime(TimeOnly.MaxValue).AddDays(1)));

            var uids = await client.Inbox.SearchAsync(query, ct);
            var lines = new List<string>();
            // Newest first, cap
            foreach (var uid in uids.Reverse().Take(40))
            {
                ct.ThrowIfCancellationRequested();
                var msg = await client.Inbox.GetMessageAsync(uid, ct);
                var day = (msg.Date.DateTime).ToString("yyyy-MM-dd");
                var subj = string.IsNullOrWhiteSpace(msg.Subject) ? "(no subject)" : msg.Subject.Trim();
                var fromAddr = msg.From?.Mailboxes.FirstOrDefault()?.Address ?? "";
                lines.Add($"{day}: Mail — {subj}" + (fromAddr.Length > 0 ? $" (from {fromAddr})" : ""));
            }

            if (lines.Count == 0)
                return "(Mail: no messages in this period.)";

            return "## Mail activity\n" + string.Join("\n", lines);
        }
        finally
        {
            if (client.IsConnected)
                await client.DisconnectAsync(true, ct);
        }
    }
}
