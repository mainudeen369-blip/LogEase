"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { clearSession, getStoredUser, getToken, gql, setSession } from "../lib/api";
import { initials, type StoredUser } from "../lib/auth";
import { WorkCopilotBot } from "./WorkCopilotBot";
import { useEffect, useState } from "react";

const nav = [
  { href: "/compose", label: "AI Entry" },
  { href: "/notes", label: "Sticky notes" },
  { href: "/worklogs", label: "Worklogs" },
  { href: "/ops", label: "AI Ops" },
  { href: "/admin", label: "Admin" },
  { href: "/admin/auto-test", label: "Auto Test" },
  { href: "/download", label: "Windows app" },
  { href: "/settings", label: "Settings" },
];

type MeData = {
  me: {
    name: string;
    email: string;
    profilePhotoUrl?: string | null;
  } | null;
};

export function AppShell({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const router = useRouter();
  const [user, setUser] = useState<StoredUser | null>(null);
  // Keep false until mount so SSR HTML matches the first client render.
  const [signedIn, setSignedIn] = useState(false);

  useEffect(() => {
    const token = getToken();
    setSignedIn(Boolean(token));
    setUser(getStoredUser());
    if (!token) return;
    gql<MeData>(
      `query { me { name email profilePhotoUrl } }`,
      undefined,
      token,
    )
      .then((data) => {
        if (!data.me) return;
        const next = {
          name: data.me.name,
          email: data.me.email,
          profilePhotoUrl: data.me.profilePhotoUrl,
        };
        setSession(token, next);
        setUser(next);
      })
      .catch(() => {});
  }, [pathname]);

  function logout() {
    clearSession();
    setSignedIn(false);
    setUser(null);
    router.push("/login");
  }

  const isAuthPage = pathname === "/login" || pathname === "/";

  if (isAuthPage) {
    return <>{children}</>;
  }

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand">
          <p className="brand-mark">LOGEASE</p>
          <p className="brand-title">Worklog Copilot</p>
        </div>

        {signedIn && user && (
          <div className="profile-card">
            {user.profilePhotoUrl ? (
              // eslint-disable-next-line @next/next/no-img-element
              <img
                src={user.profilePhotoUrl}
                alt=""
                className="profile-avatar"
                referrerPolicy="no-referrer"
              />
            ) : (
              <span className="profile-avatar profile-avatar-fallback" aria-hidden="true">
                {initials(user.name)}
              </span>
            )}
            <div className="profile-meta">
              <p className="profile-name">{user.name}</p>
              <p className="profile-email">{user.email}</p>
            </div>
          </div>
        )}

        <nav className="nav">
          {nav.map((item) => (
            <Link
              key={item.href}
              href={item.href}
              className={pathname === item.href ? "nav-link active" : "nav-link"}
            >
              {item.label}
            </Link>
          ))}
        </nav>
        <div className="sidebar-foot">
          <button type="button" className="linkish" onClick={logout}>
            Sign out
          </button>
        </div>
      </aside>
      <main className="main">
        {children}
        {signedIn && <WorkCopilotBot />}
      </main>
    </div>
  );
}
