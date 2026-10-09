"use client";

import { useEffect, useState } from "react";
import { getToken, gql } from "../lib/api";

type ReminderStatus = {
  reminderStatus: {
    shouldRemind: boolean;
    message: string;
    reminderTime: string;
    enabled: boolean;
  };
};

export function ReminderBanner() {
  const [msg, setMsg] = useState<string | null>(null);
  const [urgent, setUrgent] = useState(false);

  useEffect(() => {
    const token = getToken();
    if (!token) return;
    gql<ReminderStatus>(
      `query { reminderStatus { shouldRemind message reminderTime enabled } }`,
      undefined,
      token,
    )
      .then((d) => {
        setMsg(d.reminderStatus.message);
        setUrgent(d.reminderStatus.shouldRemind);
        if (d.reminderStatus.shouldRemind && "Notification" in window) {
          if (Notification.permission === "granted") {
            new Notification("LogEase reminder", {
              body: d.reminderStatus.message,
            });
          }
        }
      })
      .catch(() => {});
  }, []);

  if (!msg) return null;

  return (
    <div className={urgent ? "banner banner-urgent" : "banner"}>
      <span>{msg}</span>
      {urgent && (
        <a href="/compose" className="banner-cta">
          Log now →
        </a>
      )}
    </div>
  );
}
