"use client";

import { FormEvent, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { getToken, gql } from "../../lib/api";

type KimaiUser = {
  id: number;
  username: string;
  alias?: string | null;
  email?: string | null;
  title?: string | null;
  displayName: string;
};

type MePayload = {
  reminderEnabled: boolean;
  reminderTime: string;
  hasKimaiToken: boolean;
  kimaiError?: string | null;
  kimaiUser?: KimaiUser | null;
  hasGitlabToken?: boolean;
  gitlabBaseUrl?: string | null;
  gitlabUsername?: string | null;
  gitlabConnectedUser?: string | null;
  gitlabError?: string | null;
  hasMailSettings?: boolean;
  mailImapHost?: string | null;
  mailImapPort?: number | null;
  mailImapUser?: string | null;
  mailImapUseSsl?: boolean;
};

const ME_FIELDS = `
  reminderEnabled reminderTime hasKimaiToken kimaiError
  kimaiUser { id username alias email title displayName }
  hasGitlabToken gitlabBaseUrl gitlabUsername gitlabConnectedUser gitlabError
  hasMailSettings mailImapHost mailImapPort mailImapUser mailImapUseSsl
`;

export default function SettingsPage() {
  const router = useRouter();
  const [enabled, setEnabled] = useState(true);
  const [time, setTime] = useState("17:45");
  const [hasKimaiToken, setHasKimaiToken] = useState(false);
  const [kimaiToken, setKimaiToken] = useState("");
  const [kimaiUser, setKimaiUser] = useState<KimaiUser | null>(null);
  const [kimaiError, setKimaiError] = useState("");
  const [gitlabToken, setGitlabToken] = useState("");
  const [gitlabBaseUrl, setGitlabBaseUrl] = useState("https://gitlab.com");
  const [gitlabUsername, setGitlabUsername] = useState("");
  const [hasGitlabToken, setHasGitlabToken] = useState(false);
  const [gitlabConnectedUser, setGitlabConnectedUser] = useState<string | null>(null);
  const [gitlabError, setGitlabError] = useState("");
  const [mailHost, setMailHost] = useState("");
  const [mailPort, setMailPort] = useState("993");
  const [mailUser, setMailUser] = useState("");
  const [mailPassword, setMailPassword] = useState("");
  const [mailSsl, setMailSsl] = useState(true);
  const [hasMailSettings, setHasMailSettings] = useState(false);
  const [msg, setMsg] = useState("");
  const [error, setError] = useState("");

  function applyMe(me: MePayload) {
    setEnabled(me.reminderEnabled);
    setTime(me.reminderTime);
    setHasKimaiToken(me.hasKimaiToken);
    setKimaiUser(me.kimaiUser ?? null);
    setKimaiError(me.kimaiError ?? "");
    setHasGitlabToken(Boolean(me.hasGitlabToken));
    setGitlabConnectedUser(me.gitlabConnectedUser ?? null);
    setGitlabError(me.gitlabError ?? "");
    if (me.gitlabBaseUrl) setGitlabBaseUrl(me.gitlabBaseUrl);
    if (me.gitlabUsername) setGitlabUsername(me.gitlabUsername);
    setHasMailSettings(Boolean(me.hasMailSettings));
    if (me.mailImapHost) setMailHost(me.mailImapHost);
    if (me.mailImapPort) setMailPort(String(me.mailImapPort));
    if (me.mailImapUser) setMailUser(me.mailImapUser);
    if (typeof me.mailImapUseSsl === "boolean") setMailSsl(me.mailImapUseSsl);
  }

  useEffect(() => {
    const token = getToken();
    if (!token) {
      router.replace("/login");
      return;
    }
    gql<{ me: MePayload }>(`query { me { ${ME_FIELDS} } }`, undefined, token)
      .then((d) => {
        if (d.me) applyMe(d.me);
      })
      .catch((e) => setError(e.message));
  }, [router]);

  async function onSave(e: FormEvent) {
    e.preventDefault();
    const token = getToken();
    if (!token) return;
    setMsg("");
    setError("");
    const savedParts: string[] = [];
    try {
      await gql(
        `mutation ($input: UpdateReminderInput!) {
          updateReminder(input: $input) { reminderEnabled reminderTime }
        }`,
        { input: { enabled, reminderTime: time } },
        token,
      );

      if (kimaiToken.trim()) {
        const result = await gql<{ updateKimaiSettings: MePayload }>(
          `mutation ($input: UpdateKimaiSettingsInput!) {
            updateKimaiSettings(input: $input) { ${ME_FIELDS} }
          }`,
          { input: { apiToken: kimaiToken.trim() } },
          token,
        );
        applyMe(result.updateKimaiSettings);
        setKimaiToken("");
        savedParts.push(
          result.updateKimaiSettings.kimaiUser
            ? `Kimai connected as ${result.updateKimaiSettings.kimaiUser.displayName}.`
            : "Kimai token saved.",
        );
      }

      // Optional: only push GitLab when user pastes a new token (skip re-validating expired tokens on every Save).
      if (gitlabToken.trim().length > 0) {
        const result = await gql<{ updateGitlabSettings: MePayload }>(
          `mutation ($input: UpdateGitlabSettingsInput!) {
            updateGitlabSettings(input: $input) { ${ME_FIELDS} }
          }`,
          {
            input: {
              personalAccessToken: gitlabToken.trim(),
              baseUrl: gitlabBaseUrl.trim() || null,
              username: gitlabUsername.trim() || null,
            },
          },
          token,
        );
        applyMe(result.updateGitlabSettings);
        setGitlabToken("");
        if (result.updateGitlabSettings.gitlabConnectedUser) {
          const line = `GitLab connected as ${result.updateGitlabSettings.gitlabConnectedUser}.`;
          savedParts.push(line);
          window.alert(`GitLab token saved.\n\n${result.updateGitlabSettings.gitlabConnectedUser}`);
        } else if (result.updateGitlabSettings.gitlabError) {
          savedParts.push("GitLab optional — token rejected (see GitLab section).");
        } else if (result.updateGitlabSettings.hasGitlabToken) {
          savedParts.push("GitLab token saved.");
          window.alert("GitLab token saved.");
        }
      }

      if (mailHost.trim() || mailUser.trim() || mailPassword.trim()) {
        const portNum = Number(mailPort);
        const result = await gql<{ updateMailSettings: MePayload }>(
          `mutation ($input: UpdateMailSettingsInput!) {
            updateMailSettings(input: $input) { ${ME_FIELDS} }
          }`,
          {
            input: {
              imapHost: mailHost.trim() || null,
              imapPort: Number.isFinite(portNum) ? portNum : null,
              imapUser: mailUser.trim() || null,
              imapPassword: mailPassword.trim() || null,
              useSsl: mailSsl,
            },
          },
          token,
        );
        applyMe(result.updateMailSettings);
        setMailPassword("");
        if (result.updateMailSettings.hasMailSettings) {
          savedParts.push("Mail IMAP settings saved.");
          window.alert("Mail IMAP settings saved.");
        }
      }

      setMsg(savedParts.length > 0 ? savedParts.join(" ") : "Settings saved.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Save failed");
    }
  }

  async function clearKimaiToken() {
    const token = getToken();
    if (!token) return;
    setMsg("");
    setError("");
    try {
      const result = await gql<{ updateKimaiSettings: MePayload }>(
        `mutation {
          updateKimaiSettings(input: { apiToken: "" }) { ${ME_FIELDS} }
        }`,
        undefined,
        token,
      );
      applyMe(result.updateKimaiSettings);
      setKimaiToken("");
      setMsg("Kimai token removed.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Remove failed");
    }
  }

  return (
    <div>
      <h1 className="page-title">Settings</h1>
      <p className="page-sub">
        Reminder and Kimai settings are saved to your account and used by both web
        and the Windows tray app.
      </p>

      <div className="card" style={{ maxWidth: 520, marginBottom: "1rem" }}>
        <h2 style={{ marginTop: 0 }}>Worklog reminder</h2>
        <form onSubmit={onSave}>
          <div className="field">
            <label htmlFor="enabled">Reminder</label>
            <select
              id="enabled"
              value={enabled ? "on" : "off"}
              onChange={(e) => setEnabled(e.target.value === "on")}
            >
              <option value="on">On</option>
              <option value="off">Off</option>
            </select>
          </div>
          <div className="field">
            <label htmlFor="time">Office end time</label>
            <input
              id="time"
              type="time"
              value={time}
              onChange={(e) => setTime(e.target.value)}
            />
          </div>

          <h2>Kimai</h2>
          <p className="page-sub" style={{ marginTop: 0 }}>
            Paste your personal Kimai API token from Kimai Profile → API Access.
            LogEase reads your Kimai profile with that token and shows it below.
          </p>

          {kimaiUser && (
            <div
              className="card"
              style={{
                marginBottom: "0.9rem",
                padding: "0.85rem 1rem",
                background: "#f4f6fb",
                border: "1px solid var(--line)",
              }}
            >
              <div style={{ fontSize: "0.75rem", fontWeight: 700, letterSpacing: "0.04em", color: "var(--muted)" }}>
                CONNECTED KIMAI USER
              </div>
              <div style={{ marginTop: "0.35rem", fontWeight: 600 }}>{kimaiUser.displayName}</div>
              <div className="page-sub" style={{ margin: "0.25rem 0 0", fontSize: "0.85rem" }}>
                {kimaiUser.email ? `${kimaiUser.email} · ` : ""}
                @{kimaiUser.username}
                {kimaiUser.title ? ` · ${kimaiUser.title}` : ""}
                {` · id ${kimaiUser.id}`}
              </div>
            </div>
          )}

          {!kimaiUser && hasKimaiToken && kimaiError && (
            <p className="error" style={{ marginTop: 0 }}>
              Token saved, but Kimai profile could not be loaded: {kimaiError}
            </p>
          )}

          <div className="field">
            <label htmlFor="kimaiToken">Kimai API token</label>
            <input
              id="kimaiToken"
              type="password"
              value={kimaiToken}
              onChange={(e) => setKimaiToken(e.target.value)}
              placeholder={
                hasKimaiToken
                  ? "Token configured — enter a new value to replace"
                  : "Paste Kimai API token"
              }
              autoComplete="off"
            />
          </div>
          {hasKimaiToken && (
            <button
              className="btn-ghost"
              type="button"
              onClick={clearKimaiToken}
              style={{ marginBottom: "1rem" }}
            >
              Remove Kimai token
            </button>
          )}

          <h2>GitLab (Phase B)</h2>
          <p className="page-sub" style={{ marginTop: 0 }}>
            Personal access token with <code>read_api</code> / <code>read_user</code>. Used to
            fetch your events when summarizing worklogs. If you use <strong>company GitLab</strong>{" "}
            (not gitlab.com), set the correct base URL first — e.g.{" "}
            <code>https://gitlab.yourcompany.com</code>.
          </p>

          {gitlabConnectedUser && (
            <div
              className="card"
              style={{
                marginBottom: "0.9rem",
                padding: "0.85rem 1rem",
                background: "#ecfdf5",
                border: "1px solid #99f6e4",
              }}
            >
              <div style={{ fontSize: "0.75rem", fontWeight: 700, letterSpacing: "0.04em", color: "var(--muted)" }}>
                CONNECTED GITLAB USER
              </div>
              <div style={{ marginTop: "0.35rem", fontWeight: 600 }}>{gitlabConnectedUser}</div>
              {gitlabBaseUrl && (
                <div className="page-sub" style={{ margin: "0.25rem 0 0", fontSize: "0.85rem" }}>
                  {gitlabBaseUrl}
                  {gitlabUsername ? ` · @${gitlabUsername}` : ""}
                </div>
              )}
            </div>
          )}

          {!gitlabConnectedUser && hasGitlabToken && gitlabError && (
            <div className="error" style={{ marginTop: 0 }}>
              <p style={{ margin: "0 0 0.5rem" }}>
                Token saved, but GitLab profile could not be loaded: {gitlabError}
              </p>
              <p className="page-sub" style={{ margin: 0, fontSize: "0.85rem" }}>
                Trying <code>{gitlabBaseUrl || "https://gitlab.com"}</code>. Fix base URL if your
                token was created on a different GitLab host, then paste the token again and Save.
              </p>
            </div>
          )}

          {!hasGitlabToken && !gitlabToken && (
            <p className="page-sub" style={{ marginTop: 0, color: "var(--warn)" }}>
              GitLab token not saved yet — paste token below and click Save.
            </p>
          )}
          <div className="field">
            <label htmlFor="glBase">GitLab base URL</label>
            <input
              id="glBase"
              value={gitlabBaseUrl}
              onChange={(e) => setGitlabBaseUrl(e.target.value)}
              placeholder="https://gitlab.com"
            />
          </div>
          <div className="field">
            <label htmlFor="glUser">Username (optional)</label>
            <input
              id="glUser"
              value={gitlabUsername}
              onChange={(e) => setGitlabUsername(e.target.value)}
              placeholder="your.username"
            />
          </div>
          <div className="field">
            <label htmlFor="glToken">Personal access token</label>
            <input
              id="glToken"
              type="password"
              value={gitlabToken}
              onChange={(e) => setGitlabToken(e.target.value)}
              placeholder={
                hasGitlabToken
                  ? "Token configured — enter a new value to replace"
                  : "glpat-…"
              }
              autoComplete="off"
            />
          </div>

          <h2>Mail IMAP (Phase B)</h2>
          <p className="page-sub" style={{ marginTop: 0 }}>
            Read-only inbox scan for subject lines in the date range (e.g. Outlook /
            office365 IMAP app password).
          </p>
          <div className="field">
            <label htmlFor="mailHost">IMAP host</label>
            <input
              id="mailHost"
              value={mailHost}
              onChange={(e) => setMailHost(e.target.value)}
              placeholder="outlook.office365.com"
            />
          </div>
          <div className="field">
            <label htmlFor="mailPort">Port</label>
            <input
              id="mailPort"
              value={mailPort}
              onChange={(e) => setMailPort(e.target.value)}
              placeholder="993"
            />
          </div>
          <div className="field">
            <label htmlFor="mailUser">IMAP user</label>
            <input
              id="mailUser"
              value={mailUser}
              onChange={(e) => setMailUser(e.target.value)}
              placeholder="you@company.com"
            />
          </div>
          <div className="field">
            <label htmlFor="mailPass">IMAP password / app password</label>
            <input
              id="mailPass"
              type="password"
              value={mailPassword}
              onChange={(e) => setMailPassword(e.target.value)}
              placeholder={
                hasMailSettings
                  ? "Configured — enter a new value to replace"
                  : "App password"
              }
              autoComplete="off"
            />
          </div>
          <div className="field">
            <label htmlFor="mailSsl">SSL</label>
            <select
              id="mailSsl"
              value={mailSsl ? "on" : "off"}
              onChange={(e) => setMailSsl(e.target.value === "on")}
            >
              <option value="on">On</option>
              <option value="off">Off</option>
            </select>
          </div>

          {msg && <p className="ok">{msg}</p>}
          {error && <p className="error">{error}</p>}
          <button className="btn" type="submit">
            Save
          </button>
        </form>
      </div>
    </div>
  );
}
