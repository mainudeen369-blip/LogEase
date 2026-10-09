"use client";

import { FormEvent, useEffect, useRef, useState } from "react";
import { getToken, gql } from "../lib/api";
import {
  askWorkCopilot,
  defaultDateRange,
  QUICK_PROMPTS,
  type CopilotReply,
  type CopilotRow,
} from "../lib/workCopilot";

type ChatMessage = {
  id: string;
  role: "user" | "bot";
  text: string;
  rows?: CopilotRow[];
};

export function WorkCopilotBot() {
  const [open, setOpen] = useState(false);
  const [input, setInput] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [messages, setMessages] = useState<ChatMessage[]>([
    {
      id: "welcome",
      role: "bot",
      text:
        "Hi — I'm your work copilot. Ask what you did, summarize sticky notes, or paste messy text. " +
        "I'll reply with a structured worklog table you can save.",
    },
  ]);
  const [lastRows, setLastRows] = useState<CopilotRow[]>([]);
  const scrollRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (open && scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [messages, open, busy]);

  async function send(text: string) {
    const token = getToken();
    if (!token || !text.trim()) return;
    setError("");
    setBusy(true);
    const userMsg: ChatMessage = { id: `u-${Date.now()}`, role: "user", text: text.trim() };
    setMessages((m) => [...m, userMsg]);
    setInput("");

    try {
      const reply: CopilotReply = await askWorkCopilot(text, token, gql);
      if (reply.rows?.length) setLastRows(reply.rows);
      setMessages((m) => [
        ...m,
        {
          id: `b-${Date.now()}`,
          role: "bot",
          text: reply.text,
          rows: reply.rows,
        },
      ]);
    } catch (e) {
      const msg = e instanceof Error ? e.message : "Request failed";
      setError(msg);
      setMessages((m) => [...m, { id: `e-${Date.now()}`, role: "bot", text: msg }]);
    } finally {
      setBusy(false);
    }
  }

  function onSubmit(e: FormEvent) {
    e.preventDefault();
    send(input);
  }

  async function saveRows(rows: CopilotRow[]) {
    const token = getToken();
    if (!token) return;
    const items = rows
      .filter((r) => r.projectId && r.activityId)
      .map((r) => ({
        workDate: r.workDate,
        projectId: r.projectId,
        activityId: r.activityId,
        hours: r.hours,
        description: r.description,
      }));
    if (items.length === 0) {
      setError("Rows need matched project/activity IDs before save.");
      return;
    }
    setBusy(true);
    setError("");
    try {
      const d = await gql<{ saveWorklogs: { savedCount: number; destination: string } }>(
        `mutation ($input: SaveWorklogsInput!) {
          saveWorklogs(input: $input) { savedCount destination }
        }`,
        { input: { items } },
        token,
      );
      setMessages((m) => [
        ...m,
        {
          id: `s-${Date.now()}`,
          role: "bot",
          text: `Saved ${d.saveWorklogs.savedCount} row(s) to ${d.saveWorklogs.destination}.`,
        },
      ]);
      setLastRows([]);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Save failed");
    } finally {
      setBusy(false);
    }
  }

  const range = defaultDateRange();

  return (
    <>
      <button
        type="button"
        className="copilot-fab"
        onClick={() => setOpen((v) => !v)}
        aria-label={open ? "Close work copilot" : "Open work copilot"}
        title="Work copilot"
      >
        {open ? "×" : "◆"}
      </button>

      {open && (
        <div className="copilot-panel" role="dialog" aria-label="Work copilot">
          <header className="copilot-header">
            <div>
              <strong>Work copilot</strong>
              <p className="copilot-sub">
                Summarize · parse · structured rows ({range.from} → {range.to})
              </p>
            </div>
            <button type="button" className="copilot-close" onClick={() => setOpen(false)}>
              ×
            </button>
          </header>

          <div className="copilot-quick">
            {QUICK_PROMPTS.map((q) => (
              <button key={q} type="button" disabled={busy} onClick={() => send(q)}>
                {q}
              </button>
            ))}
          </div>

          <div className="copilot-messages" ref={scrollRef}>
            {messages.map((m) => (
              <div key={m.id} className={m.role === "user" ? "copilot-msg user" : "copilot-msg bot"}>
                <p style={{ whiteSpace: "pre-wrap", margin: 0 }}>{m.text}</p>
                {m.rows && m.rows.length > 0 && (
                  <div className="copilot-table-wrap">
                    <table className="data">
                      <thead>
                        <tr>
                          <th>Date</th>
                          <th>Project</th>
                          <th>Activity</th>
                          <th>Hrs</th>
                          <th>Description</th>
                        </tr>
                      </thead>
                      <tbody>
                        {m.rows.map((r, i) => (
                          <tr key={i}>
                            <td>{r.workDate}</td>
                            <td>{r.projectName}</td>
                            <td>{r.activityName}</td>
                            <td>{r.hours}</td>
                            <td>{r.description}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                    <button
                      type="button"
                      className="btn"
                      disabled={busy}
                      onClick={() => saveRows(m.rows!)}
                      style={{ marginTop: "0.5rem" }}
                    >
                      Save these rows
                    </button>
                  </div>
                )}
              </div>
            ))}
            {busy && <p className="copilot-sub">Thinking…</p>}
          </div>

          {error && <p className="error copilot-error">{error}</p>}

          <form className="copilot-input-row" onSubmit={onSubmit}>
            <input
              value={input}
              onChange={(e) => setInput(e.target.value)}
              placeholder="Ask or paste work notes…"
              disabled={busy}
              aria-label="Message work copilot"
            />
            <button className="btn" type="submit" disabled={busy || !input.trim()}>
              Send
            </button>
          </form>

          {lastRows.length > 0 && (
            <p className="copilot-sub" style={{ padding: "0 0.75rem 0.75rem" }}>
              Tip: open <strong>AI Entry</strong> to edit rows before save.
            </p>
          )}
        </div>
      )}
    </>
  );
}
