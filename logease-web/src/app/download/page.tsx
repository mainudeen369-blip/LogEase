"use client";

import { useState } from "react";

const steps = [
  {
    n: 1,
    title: "Download",
    body: "Click Download below. Your browser saves LogEase.Desktop.exe.",
  },
  {
    n: 2,
    title: "Run the file",
    body: "Open the downloaded exe (Windows may ask “Do you want to run this app?” — choose Yes / Run anyway).",
  },
  {
    n: 3,
    title: "Install when asked",
    body: "LogEase opens a window: “Would you like to install?” Choose Yes to copy itself to AppData, create Desktop & Start Menu shortcuts, and optionally start with Windows.",
  },
  {
    n: 4,
    title: "Find the tray icon",
    body: "Look near the clock for the LogEase icon (teal L). Right-click → Settings for API URL & login, then Log work…",
  },
];

export default function DownloadPage() {
  const [downloaded, setDownloaded] = useState(false);

  return (
    <div>
      <h1 className="page-title">Windows app</h1>
      <p className="page-sub">
        Tray reminder + free-text worklog entry. After download, the app asks if you want to install
        and walks you through the steps.
      </p>

      <div className="card" style={{ maxWidth: 640, marginBottom: "1rem" }}>
        <div style={{ display: "flex", gap: "1rem", alignItems: "center", marginBottom: "1rem" }}>
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            src="/downloads/logease-icon.png"
            alt="LogEase"
            width={56}
            height={56}
            style={{ borderRadius: 12 }}
          />
          <div>
            <h2 style={{ margin: 0, fontSize: "1.2rem" }}>LogEase Desktop</h2>
            <p className="page-sub" style={{ margin: "0.25rem 0 0" }}>
              Needs API at <code>http://localhost:5055/graphql</code> (or your server URL).
            </p>
          </div>
        </div>

        <div className="btn-row">
          <a
            className="btn"
            href="/downloads/LogEase.Desktop.exe"
            download
            onClick={() => setDownloaded(true)}
          >
            Download LogEase.Desktop.exe
          </a>
        </div>

        {downloaded && (
          <div
            className="banner"
            style={{ marginTop: "1rem", flexDirection: "column", alignItems: "flex-start" }}
          >
            <strong>Downloaded — what next?</strong>
            <p style={{ margin: "0.35rem 0 0" }}>
              Open the file from your Downloads folder. When LogEase asks{" "}
              <em>“Would you like to install?”</em>, choose <strong>Yes, install</strong> (recommended).
              Then look for the tray icon near the clock.
            </p>
          </div>
        )}
      </div>

      <div className="card" style={{ maxWidth: 640 }}>
        <h2 style={{ margin: "0 0 0.75rem", fontSize: "1.05rem" }}>Install steps</h2>
        <ol style={{ listStyle: "none", margin: 0, padding: 0, display: "grid", gap: "0.85rem" }}>
          {steps.map((s) => (
            <li
              key={s.n}
              style={{
                display: "grid",
                gridTemplateColumns: "2.25rem 1fr",
                gap: "0.75rem",
                alignItems: "start",
              }}
            >
              <span
                style={{
                  width: "2rem",
                  height: "2rem",
                  borderRadius: "999px",
                  background: "var(--accent-soft)",
                  color: "var(--accent)",
                  display: "grid",
                  placeItems: "center",
                  fontWeight: 700,
                  fontSize: "0.85rem",
                }}
              >
                {s.n}
              </span>
              <div>
                <div style={{ fontWeight: 700 }}>{s.title}</div>
                <div style={{ color: "var(--muted)", fontSize: "0.92rem", marginTop: 2 }}>{s.body}</div>
              </div>
            </li>
          ))}
        </ol>
      </div>
    </div>
  );
}
