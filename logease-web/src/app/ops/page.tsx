"use client";

import { FormEvent, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { getToken, gql } from "../../lib/api";

type ProviderCount = { parser: string; count: number };
type ParseEvent = {
  id: string;
  parser: string;
  rowCount: number;
  durationMs: number;
  usedFallback: boolean;
  success: boolean;
  warning?: string | null;
  source: string;
  createdAt: string;
};
type Observability = {
  totalParses: number;
  fallbackCount: number;
  successCount: number;
  fallbackRatePct: number;
  avgDurationMs: number;
  byParser: ProviderCount[];
  recent: ParseEvent[];
};
type Incident = {
  id: string;
  title: string;
  detail: string;
  severity: string;
  status: string;
  category: string;
  createdAt: string;
  updatedAt: string;
  resolvedAt?: string | null;
};
type Debt = {
  id: string;
  title: string;
  description: string;
  area: string;
  priority: string;
  status: string;
  updatedAt: string;
};

const INCIDENT_STATUSES = ["Open", "Investigating", "Mitigated", "Resolved"] as const;
const DEBT_STATUSES = ["Open", "InProgress", "Done"] as const;

export default function AiOpsPage() {
  const router = useRouter();
  const [obs, setObs] = useState<Observability | null>(null);
  const [incidents, setIncidents] = useState<Incident[]>([]);
  const [debt, setDebt] = useState<Debt[]>([]);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const [incTitle, setIncTitle] = useState("");
  const [incDetail, setIncDetail] = useState("");
  const [incSeverity, setIncSeverity] = useState("Medium");
  const [incCategory, setIncCategory] = useState("AI");

  const [debtTitle, setDebtTitle] = useState("");
  const [debtDesc, setDebtDesc] = useState("");
  const [debtArea, setDebtArea] = useState("AI");
  const [debtPriority, setDebtPriority] = useState("Medium");

  async function load(token: string) {
    const data = await gql<{
      observabilitySummary: Observability;
      opsIncidents: Incident[];
      techDebtItems: Debt[];
    }>(
      `query {
        observabilitySummary {
          totalParses
          fallbackCount
          successCount
          fallbackRatePct
          avgDurationMs
          byParser { parser count }
          recent {
            id parser rowCount durationMs usedFallback success warning source createdAt
          }
        }
        opsIncidents {
          id title detail severity status category createdAt updatedAt resolvedAt
        }
        techDebtItems {
          id title description area priority status updatedAt
        }
      }`,
      undefined,
      token,
    );
    setObs(data.observabilitySummary);
    setIncidents(data.opsIncidents);
    setDebt(data.techDebtItems);
  }

  useEffect(() => {
    const token = getToken();
    if (!token) {
      router.replace("/login");
      return;
    }
    load(token).catch((e) => setError(e instanceof Error ? e.message : "Failed to load AI Ops"));
  }, [router]);

  async function refresh() {
    const token = getToken();
    if (!token) return;
    setBusy(true);
    setError("");
    try {
      await load(token);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Refresh failed");
    } finally {
      setBusy(false);
    }
  }

  async function onCreateIncident(e: FormEvent) {
    e.preventDefault();
    const token = getToken();
    if (!token || !incTitle.trim()) return;
    setBusy(true);
    setError("");
    try {
      await gql(
        `mutation ($input: CreateOpsIncidentInput!) {
          createOpsIncident(input: $input) { id }
        }`,
        {
          input: {
            title: incTitle.trim(),
            detail: incDetail.trim(),
            severity: incSeverity,
            category: incCategory,
          },
        },
        token,
      );
      setIncTitle("");
      setIncDetail("");
      await load(token);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not create incident");
    } finally {
      setBusy(false);
    }
  }

  async function setIncidentStatus(id: string, status: string) {
    const token = getToken();
    if (!token) return;
    setBusy(true);
    try {
      await gql(
        `mutation ($input: UpdateOpsIncidentInput!) {
          updateOpsIncident(input: $input) { id status }
        }`,
        { input: { id, status } },
        token,
      );
      await load(token);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Update failed");
    } finally {
      setBusy(false);
    }
  }

  async function onCreateDebt(e: FormEvent) {
    e.preventDefault();
    const token = getToken();
    if (!token || !debtTitle.trim()) return;
    setBusy(true);
    setError("");
    try {
      await gql(
        `mutation ($input: CreateTechDebtInput!) {
          createTechDebtItem(input: $input) { id }
        }`,
        {
          input: {
            title: debtTitle.trim(),
            description: debtDesc.trim(),
            area: debtArea,
            priority: debtPriority,
          },
        },
        token,
      );
      setDebtTitle("");
      setDebtDesc("");
      await load(token);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not create tech debt");
    } finally {
      setBusy(false);
    }
  }

  async function setDebtStatus(id: string, status: string) {
    const token = getToken();
    if (!token) return;
    setBusy(true);
    try {
      await gql(
        `mutation ($input: UpdateTechDebtInput!) {
          updateTechDebtItem(input: $input) { id status }
        }`,
        { input: { id, status } },
        token,
      );
      await load(token);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Update failed");
    } finally {
      setBusy(false);
    }
  }

  function fmtWhen(iso: string) {
    try {
      return new Date(iso).toLocaleString();
    } catch {
      return iso;
    }
  }

  return (
    <div>
      <h1 className="page-title">AI Ops</h1>
      <p className="page-sub">
        This page is the “health desk” for LogEase AI — not for writing worklogs, but for watching
        whether AI is working well and capturing problems so the team can fix them.
      </p>

      <div className="card" style={{ marginBottom: "1rem" }}>
        <h2 style={{ marginTop: 0 }}>What is this page for?</h2>
        <p className="page-sub" style={{ marginTop: 0 }}>
          Think of LogEase like a car. AI Entry is the steering wheel (you drive and log work).
          <strong> AI Ops</strong> is the dashboard lights and service notebook:
        </p>
        <ul className="ops-guide-list">
          <li>
            <strong>Observability</strong> — the gauges. Did AI run? Was it fast? Did it need a backup plan?
          </li>
          <li>
            <strong>Incident response</strong> — the warning light + repair ticket. Something broke right now; we track it until fixed.
          </li>
          <li>
            <strong>Technical debt</strong> — known shortcuts we took. Not broken today, but we should improve them later.
          </li>
        </ul>
        <p className="page-sub" style={{ marginBottom: 0 }}>
          <strong>How to try it:</strong> open <em>AI Entry</em>, click Generate table once or twice, then come back here and click Refresh. You should see new parse rows under Observability.
        </p>
      </div>

      <div className="card" style={{ marginBottom: "1rem" }}>
        <h2 style={{ marginTop: 0 }}>Who raises what — and who handles it?</h2>
        <p className="page-sub" style={{ marginTop: 0 }}>
          You do <strong>not</strong> need to be an ops engineer. Here is the simple split:
        </p>
        <div className="table-wrap">
          <table className="data ops-who-table">
            <thead>
              <tr>
                <th>Thing</th>
                <th>Who raises / creates it?</th>
                <th>Who handles / closes it?</th>
              </tr>
            </thead>
            <tbody>
              <tr>
                <td>
                  <strong>Observability</strong>
                  <div style={{ color: "var(--muted)", fontSize: "0.8rem" }}>
                    Parse history &amp; stats
                  </div>
                </td>
                <td>
                  <strong>Nobody manually.</strong> LogEase records this automatically every time someone
                  uses Generate on AI Entry / Notes / Windows app.
                </td>
                <td>
                  <strong>Anyone can look.</strong> Developers / admins use it to see if AI is healthy.
                  No “close” step — it is a log, not a ticket.
                </td>
              </tr>
              <tr>
                <td>
                  <strong>Incident</strong>
                  <div style={{ color: "var(--muted)", fontSize: "0.8rem" }}>
                    Live problem right now
                  </div>
                </td>
                <td>
                  <strong>Two ways:</strong>
                  <br />
                  1) <em>Automatic</em> — LogEase opens one when AI falls back to heuristic with a warning.
                  <br />
                  2) <em>Manual</em> — any logged-in user can open one here (e.g. “Kimai save failed”).
                </td>
                <td>
                  <strong>Admin / developer / whoever owns the fix</strong> updates status
                  (Investigating → Mitigated → Resolved). Employees usually only report; they do not have to “own” the fix.
                </td>
              </tr>
              <tr>
                <td>
                  <strong>Technical debt</strong>
                  <div style={{ color: "var(--muted)", fontSize: "0.8rem" }}>
                    Known shortcut for later
                  </div>
                </td>
                <td>
                  <strong>Mostly developers / admins.</strong> Also seeded at API startup with example items.
                  Employees can add one if they notice “this is messy / fragile,” but that is optional.
                </td>
                <td>
                  <strong>Developers</strong> pick items in planning and move Open → InProgress → Done when
                  the improvement is actually done. Not an emergency queue.
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <p className="page-sub" style={{ marginBottom: 0 }}>
          <strong>Rule of thumb:</strong> Incident = “fix it now.” Debt = “improve it later.” Observability = “show me what happened.”
        </p>
      </div>

      <div className="btn-row" style={{ marginBottom: "1rem" }}>
        <button type="button" className="btn btn-ghost" disabled={busy} onClick={refresh}>
          {busy ? "Refreshing…" : "Refresh"}
        </button>
      </div>
      {error && <p className="error">{error}</p>}

      {/* ── Observability ── */}
      <div className="card" style={{ marginBottom: "1rem" }}>
        <h2 style={{ marginTop: 0 }}>1. Observability — “What just happened?”</h2>
        <p className="page-sub" style={{ marginTop: 0 }}>
          <strong>In plain words:</strong> observability means we keep a simple history of AI work so we can
          answer “is AI healthy?” without guessing. Every time someone generates a worklog table, LogEase
          records which AI model ran, how long it took, how many rows came back, and whether we had to use
          the offline “heuristic” backup (when AI is slow or unavailable).
        </p>
        <p className="page-sub">
          <strong>What to look for:</strong> high fallback % means AI often failed and the backup parser took over
          (rows may be less accurate). High avg latency means Generate feels slow. Empty table? Generate once on AI Entry first.
        </p>
        {obs ? (
          <>
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit, minmax(140px, 1fr))",
                gap: "0.75rem",
                marginBottom: "1rem",
              }}
            >
              <Stat label="Total parses" value={String(obs.totalParses)} />
              <Stat label="With rows" value={String(obs.successCount)} />
              <Stat label="Fallbacks" value={String(obs.fallbackCount)} />
              <Stat label="Fallback %" value={`${obs.fallbackRatePct}%`} />
              <Stat label="Avg latency" value={`${obs.avgDurationMs} ms`} />
            </div>
            {obs.byParser.length > 0 && (
              <p className="page-sub" style={{ marginTop: 0 }}>
                By provider:{" "}
                {obs.byParser.map((p) => `${p.parser} (${p.count})`).join(" · ")}
              </p>
            )}
            <div className="table-wrap">
              <table className="data">
                <thead>
                  <tr>
                    <th>When</th>
                    <th>Parser</th>
                    <th>Rows</th>
                    <th>ms</th>
                    <th>Source</th>
                    <th>Note</th>
                  </tr>
                </thead>
                <tbody>
                  {obs.recent.length === 0 ? (
                    <tr>
                      <td colSpan={6} style={{ color: "var(--muted)" }}>
                        No parses yet — open AI Entry and Generate once.
                      </td>
                    </tr>
                  ) : (
                    obs.recent.map((e) => (
                      <tr key={e.id}>
                        <td style={{ whiteSpace: "nowrap" }}>{fmtWhen(e.createdAt)}</td>
                        <td>
                          <span className={e.usedFallback ? "chip warn" : "chip"}>{e.parser}</span>
                        </td>
                        <td>{e.rowCount}</td>
                        <td>{e.durationMs}</td>
                        <td>{e.source}</td>
                        <td style={{ color: "var(--muted)", fontSize: "0.85rem" }}>
                          {e.warning || (e.success ? "ok" : "no rows")}
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </>
        ) : (
          <p className="page-sub">Loading…</p>
        )}
      </div>

      {/* ── Incident response ── */}
      <div className="card" style={{ marginBottom: "1rem" }}>
        <h2 style={{ marginTop: 0 }}>2. Incident response — “Something is wrong right now”</h2>
        <p className="page-sub" style={{ marginTop: 0 }}>
          <strong>In plain words:</strong> an incident is a live problem that affects users — for example AI keeps
          falling back, Kimai save fails, or MCP tools stop working. Incident response is the habit of writing it
          down, assigning severity, and updating status until it is fixed (so it does not get forgotten in chat).
        </p>
        <p className="page-sub">
          <strong>How to use:</strong> create a ticket below when something breaks, then move status
          Open → Investigating → Mitigated → Resolved. High = blocks work; Low = annoying but workarounds exist.
          LogEase may also auto-open an incident when AI falls back with a warning.
        </p>
        <form onSubmit={onCreateIncident} style={{ marginBottom: "1rem", maxWidth: 640 }}>
          <div className="field">
            <label htmlFor="incTitle">Manual incident</label>
            <input
              id="incTitle"
              value={incTitle}
              onChange={(e) => setIncTitle(e.target.value)}
              placeholder="e.g. Kimai token expired for office"
              required
            />
          </div>
          <div className="field">
            <label htmlFor="incDetail">Detail</label>
            <textarea
              id="incDetail"
              rows={2}
              value={incDetail}
              onChange={(e) => setIncDetail(e.target.value)}
              placeholder="What broke, who noticed, first mitigation…"
            />
          </div>
          <div className="btn-row" style={{ gap: "0.75rem", flexWrap: "wrap" }}>
            <select value={incSeverity} onChange={(e) => setIncSeverity(e.target.value)}>
              <option>Low</option>
              <option>Medium</option>
              <option>High</option>
            </select>
            <select value={incCategory} onChange={(e) => setIncCategory(e.target.value)}>
              <option>AI</option>
              <option>Kimai</option>
              <option>MCP</option>
              <option>Other</option>
            </select>
            <button className="btn" type="submit" disabled={busy}>
              Open incident
            </button>
          </div>
        </form>
        <div className="table-wrap">
          <table className="data">
            <thead>
              <tr>
                <th>Title</th>
                <th>Severity</th>
                <th>Status</th>
                <th>Updated</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {incidents.length === 0 ? (
                <tr>
                  <td colSpan={5} style={{ color: "var(--muted)" }}>
                    No incidents. Trigger a heuristic fallback or add one manually.
                  </td>
                </tr>
              ) : (
                incidents.map((i) => (
                  <tr key={i.id}>
                    <td>
                      <div>{i.title}</div>
                      <div style={{ color: "var(--muted)", fontSize: "0.8rem" }}>
                        {i.category}: {i.detail.slice(0, 120)}
                        {i.detail.length > 120 ? "…" : ""}
                      </div>
                    </td>
                    <td>
                      <span className={i.severity === "High" ? "chip warn" : "chip"}>{i.severity}</span>
                    </td>
                    <td>{i.status}</td>
                    <td style={{ whiteSpace: "nowrap" }}>{fmtWhen(i.updatedAt)}</td>
                    <td>
                      <select
                        value={i.status}
                        disabled={busy}
                        onChange={(e) => setIncidentStatus(i.id, e.target.value)}
                      >
                        {INCIDENT_STATUSES.map((s) => (
                          <option key={s} value={s}>
                            {s}
                          </option>
                        ))}
                      </select>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* ── Technical debt ── */}
      <div className="card">
        <h2 style={{ marginTop: 0 }}>3. Technical debt — “We took a shortcut on purpose”</h2>
        <p className="page-sub" style={{ marginTop: 0 }}>
          <strong>In plain words:</strong> technical debt is work we still owe ourselves. Like borrowing money:
          shipping a quick fix today is fine, but interest builds if we never repay (cleanup, tests, better design).
          Unlike an incident, debt is usually <em>not</em> an emergency — it is a known weakness we choose to track
          so it does not stay invisible forever.
        </p>
        <p className="page-sub">
          <strong>How to use:</strong> add items when you notice “we should improve this later” (missing tests, hard-coded
          config, weak prompts, etc.). Set priority High if it keeps causing pain. Move status Open → InProgress → Done
          when someone actually pays it down. The list below may include starter examples seeded for demos.
        </p>
        <form onSubmit={onCreateDebt} style={{ marginBottom: "1rem", maxWidth: 640 }}>
          <div className="field">
            <label htmlFor="debtTitle">Add debt item</label>
            <input
              id="debtTitle"
              value={debtTitle}
              onChange={(e) => setDebtTitle(e.target.value)}
              placeholder="e.g. Prompt versions not stored"
              required
            />
          </div>
          <div className="field">
            <label htmlFor="debtDesc">Why it hurts later</label>
            <textarea
              id="debtDesc"
              rows={2}
              value={debtDesc}
              onChange={(e) => setDebtDesc(e.target.value)}
              placeholder="Impact if we ignore this…"
            />
          </div>
          <div className="btn-row" style={{ gap: "0.75rem", flexWrap: "wrap" }}>
            <select value={debtArea} onChange={(e) => setDebtArea(e.target.value)}>
              <option>AI</option>
              <option>API</option>
              <option>UI</option>
              <option>Data</option>
              <option>Ops</option>
            </select>
            <select value={debtPriority} onChange={(e) => setDebtPriority(e.target.value)}>
              <option>Low</option>
              <option>Medium</option>
              <option>High</option>
            </select>
            <button className="btn" type="submit" disabled={busy}>
              Add debt
            </button>
          </div>
        </form>
        <div className="table-wrap">
          <table className="data">
            <thead>
              <tr>
                <th>Item</th>
                <th>Area</th>
                <th>Priority</th>
                <th>Status</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {debt.length === 0 ? (
                <tr>
                  <td colSpan={5} style={{ color: "var(--muted)" }}>
                    No debt items — restart API to seed defaults, or add one above.
                  </td>
                </tr>
              ) : (
                debt.map((d) => (
                  <tr key={d.id}>
                    <td>
                      <div>{d.title}</div>
                      <div style={{ color: "var(--muted)", fontSize: "0.8rem" }}>
                        {d.description.slice(0, 140)}
                        {d.description.length > 140 ? "…" : ""}
                      </div>
                    </td>
                    <td>{d.area}</td>
                    <td>
                      <span className={d.priority === "High" ? "chip warn" : "chip"}>{d.priority}</span>
                    </td>
                    <td>{d.status}</td>
                    <td>
                      <select
                        value={d.status}
                        disabled={busy}
                        onChange={(e) => setDebtStatus(d.id, e.target.value)}
                      >
                        {DEBT_STATUSES.map((s) => (
                          <option key={s} value={s}>
                            {s}
                          </option>
                        ))}
                      </select>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div
      style={{
        border: "1px solid var(--border, #ddd)",
        borderRadius: 8,
        padding: "0.75rem",
      }}
    >
      <div style={{ color: "var(--muted)", fontSize: "0.75rem" }}>{label}</div>
      <div style={{ fontSize: "1.25rem", fontWeight: 600 }}>{value}</div>
    </div>
  );
}
