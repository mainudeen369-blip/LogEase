"use client";

import { FormEvent, useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { fetchAuthConfig, type AuthConfig } from "../../lib/auth";
import { gql, setSession } from "../../lib/api";
import { handleAzureRedirect, startAzureLogin } from "../../lib/msal";
import { KimaiTokenModal } from "../../components/KimaiTokenModal";

type LoginPayload = {
  success: boolean;
  token?: string;
  error?: string;
  name?: string;
  email?: string;
  profilePhotoUrl?: string | null;
  hasKimaiToken?: boolean;
};

type LoginData = {
  login?: LoginPayload;
  loginWithAzure?: LoginPayload;
};

function finishLogin(
  payload: LoginPayload,
  emailFallback: string,
  onNeedsKimai: () => void,
  onDone: () => void,
) {
  if (!payload.success || !payload.token) return false;
  setSession(payload.token, {
    name: payload.name || "User",
    email: payload.email || emailFallback,
    profilePhotoUrl: payload.profilePhotoUrl,
  });
  if (!payload.hasKimaiToken) {
    onNeedsKimai();
    return true;
  }
  onDone();
  return true;
}

export default function LoginPage() {
  const router = useRouter();
  const [email, setEmail] = useState("demo@logease.local");
  const [password, setPassword] = useState("Demo@123");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const [azureLoading, setAzureLoading] = useState(false);
  const [authConfig, setAuthConfig] = useState<AuthConfig | null>(null);
  const [showKimaiModal, setShowKimaiModal] = useState(false);
  const [pendingToken, setPendingToken] = useState<string | null>(null);
  const azureReturnHandled = useRef(false);

  function goToApp() {
    router.push("/compose");
  }

  async function completeAzureLogin(
    idToken: string,
    accessToken: string | undefined,
    emailFallback: string,
  ): Promise<void> {
    const data = await gql<LoginData>(
      `mutation ($input: AzureLoginInput!) {
        loginWithAzure(input: $input) {
          success token error name email profilePhotoUrl hasKimaiToken
        }
      }`,
      { input: { idToken, accessToken: accessToken || null } },
    );

    const payload = data.loginWithAzure!;
    if (!payload.success || !payload.token) {
      setError(payload.error || "Azure sign-in failed");
      return;
    }

    setPendingToken(payload.token);
    finishLogin(payload, emailFallback, () => setShowKimaiModal(true), goToApp);
  }

  useEffect(() => {
    let cancelled = false;

    (async () => {
      const cfg = await fetchAuthConfig();
      if (cancelled) return;
      setAuthConfig(cfg);

      if (!cfg?.azureEnabled || !cfg.clientId || !cfg.tenantId) return;
      if (azureReturnHandled.current) return;
      azureReturnHandled.current = true;

      setAzureLoading(true);
      try {
        const result = await handleAzureRedirect(cfg);
        if (cancelled || !result?.idToken) return;
        await completeAzureLogin(
          result.idToken,
          result.accessToken,
          result.account?.username || "",
        );
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Azure sign-in failed");
        }
      } finally {
        if (!cancelled) setAzureLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- run once on mount for redirect return
  }, []);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setLoading(true);
    setError("");
    try {
      const data = await gql<LoginData>(
        `mutation ($input: LoginInput!) {
          login(input: $input) {
            success token error name email profilePhotoUrl hasKimaiToken
          }
        }`,
        { input: { email, password } },
      );
      const payload = data.login!;
      if (!payload.success || !payload.token) {
        setError(payload.error || "Login failed");
        return;
      }
      setPendingToken(payload.token);
      finishLogin(payload, email, () => setShowKimaiModal(true), goToApp);
      if ("Notification" in window && Notification.permission === "default") {
        Notification.requestPermission().catch(() => {});
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Login failed");
    } finally {
      setLoading(false);
    }
  }

  async function signInWithMicrosoft() {
    if (!authConfig?.azureEnabled || !authConfig.clientId || !authConfig.tenantId) {
      setError("Azure sign-in is not configured on the server.");
      return;
    }

    setAzureLoading(true);
    setError("");
    try {
      // Full-page redirect — matches Entra SPA URIs (.../login). Avoids popup timed_out.
      await startAzureLogin(authConfig);
    } catch (err) {
      const msg = err instanceof Error ? err.message : "Azure sign-in failed";
      if (/AADSTS50011|redirect URI/i.test(msg)) {
        setError(
          `${msg} — Entra SPA redirect URIs must include exactly: ${window.location.origin}/login`,
        );
      } else {
        setError(msg);
      }
      setAzureLoading(false);
    }
  }

  const busy = loading || azureLoading;

  return (
    <>
      <div className="auth-wrap">
        <div className="card auth-card auth-card-wide">
          <p className="brand-mark" style={{ color: "var(--accent)" }}>
            LOGEASE
          </p>
          <h1 className="page-title" style={{ fontSize: "1.6rem" }}>
            Sign in
          </h1>
          <p className="page-sub">
            Use your office Azure AD account or the demo credentials below.
          </p>

          {authConfig?.azureEnabled && (
            <>
              <button
                type="button"
                className="btn btn-microsoft"
                disabled={busy}
                onClick={signInWithMicrosoft}
              >
                <MicrosoftIcon />
                {azureLoading ? "Redirecting to Azure…" : "Sign in with Azure"}
              </button>
              <div className="auth-divider">
                <span>or continue with email</span>
              </div>
            </>
          )}

          <form onSubmit={onSubmit}>
            <div className="field">
              <label htmlFor="email">Email</label>
              <input
                id="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                autoComplete="username"
              />
            </div>
            <div className="field">
              <label htmlFor="password">Password</label>
              <input
                id="password"
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoComplete="current-password"
              />
            </div>
            {error && <p className="error">{error}</p>}
            <button className="btn" type="submit" disabled={busy}>
              {loading ? "Signing in…" : "Sign in with email"}
            </button>
          </form>

          <p className="auth-hint">
            Demo: <code>demo@logease.local</code> / <code>Demo@123</code>
          </p>
        </div>
      </div>

      {showKimaiModal && pendingToken && (
        <KimaiTokenModal
          token={pendingToken}
          onDone={goToApp}
          onSkip={goToApp}
        />
      )}
    </>
  );
}

function MicrosoftIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 21 21" aria-hidden="true">
      <rect x="1" y="1" width="9" height="9" fill="#f25022" />
      <rect x="11" y="1" width="9" height="9" fill="#7fba00" />
      <rect x="1" y="11" width="9" height="9" fill="#00a4ef" />
      <rect x="11" y="11" width="9" height="9" fill="#ffb900" />
    </svg>
  );
}
