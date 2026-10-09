import { gql } from "./api";

export type AuthConfig = {
  azureEnabled: boolean;
  tenantId?: string | null;
  clientId?: string | null;
  instance: string;
  scopes: string[];
};

export type StoredUser = {
  name: string;
  email: string;
  profilePhotoUrl?: string | null;
};

export async function fetchAuthConfig(): Promise<AuthConfig | null> {
  try {
    const data = await gql<{ authConfig: AuthConfig }>(
      `query {
        authConfig {
          azureEnabled tenantId clientId instance scopes
        }
      }`,
    );
    return data.authConfig;
  } catch {
    return null;
  }
}

export function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return "?";
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
}
