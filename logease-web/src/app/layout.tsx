import type { Metadata } from "next";
import { DM_Sans, Fraunces } from "next/font/google";
import { AppShell } from "../components/AppShell";
import "./globals.css";

const display = Fraunces({ subsets: ["latin"], variable: "--font-display" });
const body = DM_Sans({ subsets: ["latin"], variable: "--font-body" });

export const metadata: Metadata = {
  title: "LogEase — Worklog Copilot",
  description: "Turn free-text work notes into structured worklogs.",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body className={`${display.variable} ${body.variable}`}>
        <AppShell>{children}</AppShell>
      </body>
    </html>
  );
}
