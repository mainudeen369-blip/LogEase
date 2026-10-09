import {
  PublicClientApplication,
  type AuthenticationResult,
  type Configuration,
} from "@azure/msal-browser";
import type { AuthConfig } from "./auth";

let msalInstance: PublicClientApplication | null = null;
let msalInit: Promise<PublicClientApplication> | null = null;

/** Entra already has http://localhost:3000/login and http://localhost:3001/login. */
export function azureRedirectUri(): string {
  return `${window.location.origin}/login`;
}

function buildConfig(authConfig: AuthConfig): Configuration {
  const authority = `${authConfig.instance.replace(/\/$/, "")}/${authConfig.tenantId}`;
  return {
    auth: {
      clientId: authConfig.clientId!,
      authority,
      redirectUri: azureRedirectUri(),
      navigateToLoginRequestUrl: false,
    },
    cache: { cacheLocation: "sessionStorage" },
  };
}

export async function getMsal(authConfig: AuthConfig): Promise<PublicClientApplication> {
  if (msalInstance) return msalInstance;
  if (msalInit) return msalInit;

  msalInit = (async () => {
    const app = new PublicClientApplication(buildConfig(authConfig));
    await app.initialize();
    msalInstance = app;
    return app;
  })();

  return msalInit;
}

export async function handleAzureRedirect(
  authConfig: AuthConfig,
): Promise<AuthenticationResult | null> {
  const msal = await getMsal(authConfig);
  return msal.handleRedirectPromise();
}

export async function startAzureLogin(authConfig: AuthConfig): Promise<void> {
  const msal = await getMsal(authConfig);
  const scopes =
    authConfig.scopes?.length > 0
      ? authConfig.scopes
      : ["https://graph.microsoft.com/User.Read"];

  await msal.loginRedirect({
    scopes,
    prompt: "select_account",
    redirectUri: azureRedirectUri(),
  });
}
