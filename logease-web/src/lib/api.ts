const API_URL =
  process.env.NEXT_PUBLIC_LOGEASE_API_URL || "http://localhost:5055/graphql";

export type GqlError = { message: string };

export async function gql<T>(
  query: string,
  variables?: Record<string, unknown>,
  token?: string | null,
): Promise<T> {
  const res = await fetch(API_URL, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: JSON.stringify({ query, variables }),
  });

  const json = await res.json();
  if (json.errors?.length) {
    throw new Error(json.errors.map((e: GqlError) => e.message).join("; "));
  }
  return json.data as T;
}

export const TOKEN_KEY = "logease_token";
export const USER_KEY = "logease_user";

export type SessionUser = {
  name: string;
  email: string;
  profilePhotoUrl?: string | null;
};

export function getToken(): string | null {
  if (typeof window === "undefined") return null;
  return localStorage.getItem(TOKEN_KEY);
}

export function setSession(token: string, user: SessionUser) {
  localStorage.setItem(TOKEN_KEY, token);
  localStorage.setItem(USER_KEY, JSON.stringify(user));
}

export function clearSession() {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(USER_KEY);
}

export function getStoredUser(): SessionUser | null {
  if (typeof window === "undefined") return null;
  const raw = localStorage.getItem(USER_KEY);
  if (!raw) return null;
  try {
    return JSON.parse(raw) as SessionUser;
  } catch {
    return null;
  }
}
