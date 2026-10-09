"use client";

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { ReminderBanner } from "../../components/ReminderBanner";
import { getToken, gql } from "../../lib/api";

type Project = { id: string; name: string };
type Worklog = {
  id: string;
  workDate: string;
  projectName: string;
  projectId: string;
  activityName: string;
  hours: number;
  description: string;
};

export default function WorklogsPage() {
  const router = useRouter();
  const initial = useMemo(() => {
    const to = new Date();
    const from = new Date();
    from.setDate(to.getDate() - 13);
    return {
      from: from.toISOString().slice(0, 10),
      to: to.toISOString().slice(0, 10),
    };
  }, []);

  const [from, setFrom] = useState(initial.from);
  const [to, setTo] = useState(initial.to);
  const [projectId, setProjectId] = useState("");
  const [q, setQ] = useState("");
  const [projects, setProjects] = useState<Project[]>([]);
  const [rows, setRows] = useState<Worklog[]>([]);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  async function load() {
    const token = getToken();
    if (!token) {
      router.replace("/login");
      return;
    }
    setLoading(true);
    setError("");
    try {
      const data = await gql<{ worklogs: Worklog[] }>(
        `query ($from: String, $to: String, $projectId: UUID, $q: String) {
          worklogs(from: $from, to: $to, projectId: $projectId, q: $q) {
            id workDate projectName projectId activityName hours description
          }
        }`,
        {
          from,
          to,
          projectId: projectId || null,
          q: q || null,
        },
        token,
      );
      setRows(data.worklogs);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    const token = getToken();
    if (!token) {
      router.replace("/login");
      return;
    }
    gql<{ projects: Project[] }>(`query { projects { id name } }`, undefined, token)
      .then((d) => setProjects(d.projects))
      .catch(() => {});
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const total = rows.reduce((s, r) => s + Number(r.hours), 0);

  return (
    <div>
      <ReminderBanner />
      <h1 className="page-title">Worklogs</h1>
      <p className="page-sub">Filter and review what you already saved.</p>

      <div className="card">
        <div className="filters">
          <div className="field" style={{ marginBottom: 0 }}>
            <label>From</label>
            <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
          </div>
          <div className="field" style={{ marginBottom: 0 }}>
            <label>To</label>
            <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
          </div>
          <div className="field" style={{ marginBottom: 0 }}>
            <label>Project</label>
            <select value={projectId} onChange={(e) => setProjectId(e.target.value)}>
              <option value="">All</option>
              {projects.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>
          </div>
          <div className="field" style={{ marginBottom: 0 }}>
            <label>Search</label>
            <input
              value={q}
              onChange={(e) => setQ(e.target.value)}
              placeholder="Description…"
            />
          </div>
        </div>
        <div className="btn-row">
          <button className="btn" type="button" onClick={load} disabled={loading}>
            {loading ? "Loading…" : "Apply filters"}
          </button>
        </div>
        {error && <p className="error">{error}</p>}
        <p className="totals">Total hours: {total.toFixed(2)}</p>
        <div className="table-wrap">
          <table className="data">
            <thead>
              <tr>
                <th>Date</th>
                <th>Project</th>
                <th>Activity</th>
                <th>Hours</th>
                <th className="col-desc">Description</th>
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 ? (
                <tr>
                  <td colSpan={5} style={{ color: "var(--muted)" }}>
                    No worklogs yet. Use AI Entry to create some.
                  </td>
                </tr>
              ) : (
                rows.map((r) => (
                  <tr key={r.id}>
                    <td>{r.workDate}</td>
                    <td title={r.projectName}>{r.projectName}</td>
                    <td>{r.activityName}</td>
                    <td>{r.hours}</td>
                    <td className="col-desc">
                      <span className="desc-text" title={r.description}>
                        {r.description}
                      </span>
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
