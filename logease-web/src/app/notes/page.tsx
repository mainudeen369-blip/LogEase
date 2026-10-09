"use client";

import { FormEvent, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { getToken, gql } from "../../lib/api";

type StickyNote = {
  id: string;
  noteDate: string;
  body: string;
};

type Project = {
  id: string;
  name: string;
  activities: { id: string; name: string; projectId: string }[];
};

type ParsedRow = {
  workDate: string;
  projectName: string;
  activityName: string;
  hours: number;
  description: string;
  confidence: string;
  projectId?: string | null;
  activityId?: string | null;
};

function weekRange() {
  const to = new Date();
  const from = new Date();
  from.setDate(to.getDate() - 6);
  const fmt = (d: Date) => d.toISOString().slice(0, 10);
  return { from: fmt(from), to: fmt(to) };
}

export default function NotesPage() {
  const router = useRouter();
  const initial = useMemo(() => weekRange(), []);
  const [notes, setNotes] = useState<StickyNote[]>([]);
  const [noteDate, setNoteDate] = useState(initial.to);
  const [body, setBody] = useState("");
  const [from, setFrom] = useState(initial.from);
  const [to, setTo] = useState(initial.to);
  const [includeNotes, setIncludeNotes] = useState(true);
  const [includeGitlab, setIncludeGitlab] = useState(false);
  const [includeMail, setIncludeMail] = useState(false);
  const [projects, setProjects] = useState<Project[]>([]);
  const [rows, setRows] = useState<ParsedRow[]>([]);
  const [parser, setParser] = useState("");
  const [sourcesUsed, setSourcesUsed] = useState("");
  const [preview, setPreview] = useState("");
  const [error, setError] = useState("");
  const [ok, setOk] = useState("");
  const [busy, setBusy] = useState(false);
  const [savingNoteId, setSavingNoteId] = useState<string | null>(null);

  async function loadProjects(token: string) {
    const d = await gql<{ projects: Project[] }>(
      `query { projects { id name activities { id name projectId } } }`,
      undefined,
      token,
    );
    setProjects(d.projects);
  }

  async function loadNotes(token: string) {
    const d = await gql<{ stickyNotes: StickyNote[] }>(
      `query ($from: String, $to: String) {
        stickyNotes(from: $from, to: $to) { id noteDate body }
      }`,
      { from, to },
      token,
    );
    setNotes(d.stickyNotes);
  }

  useEffect(() => {
    const token = getToken();
    if (!token) {
      router.replace("/login");
      return;
    }
    loadProjects(token).catch(() => {});
    loadNotes(token).catch((e) => setError(e.message));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [router, from, to]);

  async function addNote(e: FormEvent) {
    e.preventDefault();
    const token = getToken();
    if (!token) return;
    setError("");
    setOk("");
    try {
      await gql(
        `mutation ($input: CreateStickyNoteInput!) {
          createStickyNote(input: $input) { id }
        }`,
        { input: { noteDate, body } },
        token,
      );
      setBody("");
      setOk("Note saved.");
      await loadNotes(token);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Save failed");
    }
  }

  function updateNoteLocal(id: string, patch: Partial<StickyNote>) {
    setNotes((prev) => prev.map((n) => (n.id === id ? { ...n, ...patch } : n)));
  }

  async function saveNote(note: StickyNote) {
    const token = getToken();
    if (!token) return;
    setSavingNoteId(note.id);
    setError("");
    try {
      await gql(
        `mutation ($input: UpdateStickyNoteInput!) {
          updateStickyNote(input: $input) { id noteDate body }
        }`,
        { input: { id: note.id, noteDate: note.noteDate, body: note.body } },
        token,
      );
      setOk("Note updated.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Update failed");
      await loadNotes(token);
    } finally {
      setSavingNoteId(null);
    }
  }

  async function removeNote(id: string) {
    const token = getToken();
    if (!token) return;
    try {
      await gql(`mutation ($id: UUID!) { deleteStickyNote(id: $id) }`, { id }, token);
      setNotes((prev) => prev.filter((n) => n.id !== id));
      setOk("Note deleted.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Delete failed");
    }
  }

  async function draftFromSources() {
    const token = getToken();
    if (!token) return;
    setBusy(true);
    setError("");
    setOk("");
    try {
      const d = await gql<{
        draftWorklogFromSources: {
          rows: ParsedRow[];
          parser: string;
          warning?: string | null;
          sourcesUsed: string;
          combinedPreview?: string | null;
        };
      }>(
        `mutation ($input: DraftFromSourcesInput!) {
          draftWorklogFromSources(input: $input) {
            parser warning sourcesUsed combinedPreview
            rows {
              workDate projectName activityName hours description confidence projectId activityId
            }
          }
        }`,
        {
          input: {
            dateFrom: from,
            dateTo: to,
            includeNotes,
            includeGitlab,
            includeMail,
          },
        },
        token,
      );
      const p = d.draftWorklogFromSources;
      setRows(p.rows);
      setParser(p.parser);
      setSourcesUsed(p.sourcesUsed);
      setPreview(p.combinedPreview ?? "");
      await loadProjects(token);
      setOk(
        p.warning
          ? `Draft ready (${p.parser}). ${p.warning}`
          : `Draft ready via ${p.parser}. Sources: ${p.sourcesUsed || "none"}`,
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "Draft failed");
    } finally {
      setBusy(false);
    }
  }

  function updateRow(index: number, patch: Partial<ParsedRow>) {
    setRows((prev) => prev.map((r, i) => (i === index ? { ...r, ...patch } : r)));
  }

  function onProjectChange(index: number, projectId: string) {
    const project = projects.find((p) => p.id === projectId);
    if (!project) return;
    const activity = project.activities[0];
    updateRow(index, {
      projectId,
      projectName: project.name,
      activityId: activity?.id,
      activityName: activity?.name || "",
    });
  }

  function exportToExcel() {
    if (rows.length === 0) return;
    const esc = (v: string | number) => `"${String(v ?? "").replace(/"/g, '""')}"`;
    const lines = [
      ["Date", "Project", "Activity", "Hours", "Description", "Trust"].join(","),
      ...rows.map((r) =>
        [r.workDate, r.projectName, r.activityName, r.hours, r.description, r.confidence]
          .map(esc)
          .join(","),
      ),
    ];
    const blob = new Blob(["\uFEFF" + lines.join("\n")], {
      type: "text/csv;charset=utf-8;",
    });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `logease-draft-${new Date().toISOString().slice(0, 10)}.csv`;
    a.click();
    URL.revokeObjectURL(url);
    setOk("Exported to Excel-compatible CSV.");
  }

  async function saveRows() {
    const token = getToken();
    if (!token) return;
    setBusy(true);
    setError("");
    setOk("");
    try {
      const items = rows.map((r) => {
        if (!r.projectId || !r.activityId) {
          throw new Error("Each row needs a project and activity.");
        }
        return {
          workDate: r.workDate,
          projectId: r.projectId,
          activityId: r.activityId,
          hours: Number(r.hours),
          description: r.description,
        };
      });
      const d = await gql<{
        saveWorklogs: { savedCount: number; destination: string; message?: string | null };
      }>(
        `mutation ($input: SaveWorklogsInput!) {
          saveWorklogs(input: $input) { savedCount destination message }
        }`,
        { input: { items } },
        token,
      );
      const destinationLabel =
        d.saveWorklogs.destination === "Kimai"
          ? "Kimai"
          : d.saveWorklogs.destination === "Both"
            ? "local server and Kimai"
            : "local server";
      setOk(
        d.saveWorklogs.message ??
          `Saved ${d.saveWorklogs.savedCount} worklog(s) to ${destinationLabel}.`,
      );
      setRows([]);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Save failed");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div>
      <h1 className="page-title">Sticky notes</h1>
      <p className="page-sub">
        Daily notes (Phase A). Summarize notes and optional GitLab / mail into a worklog draft —
        edit the table like AI Entry, then save.
      </p>

      {error && <p className="error">{error}</p>}
      {ok && <p className="ok">{ok}</p>}

      <div className="card" style={{ marginBottom: "1rem" }}>
        <h2 style={{ margin: "0 0 0.35rem", fontSize: "1.1rem" }}>Add note</h2>
        <p className="page-sub" style={{ marginBottom: "0.75rem" }}>
          Jot down what you worked on — one row per day is fine.
        </p>
        <form onSubmit={addNote}>
          <div className="row">
            <div className="field" style={{ marginBottom: 0 }}>
              <label htmlFor="noteDate">Date</label>
              <input
                id="noteDate"
                type="date"
                value={noteDate}
                onChange={(e) => setNoteDate(e.target.value)}
                required
              />
            </div>
          </div>
          <div className="field">
            <label htmlFor="body">Note</label>
            <textarea
              id="body"
              className="prompt-input"
              value={body}
              onChange={(e) => setBody(e.target.value)}
              rows={4}
              placeholder="What did you work on today?"
              required
            />
          </div>
          <button className="btn" type="submit">
            Save note
          </button>
        </form>
      </div>

      <div className="card" style={{ marginBottom: "1rem" }}>
        <h2 style={{ margin: "0 0 0.35rem", fontSize: "1.1rem" }}>Notes in range</h2>
        <div className="row" style={{ marginBottom: "0.5rem" }}>
          <div className="field" style={{ marginBottom: 0 }}>
            <label htmlFor="from">From</label>
            <input id="from" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
          </div>
          <div className="field" style={{ marginBottom: 0 }}>
            <label htmlFor="to">To</label>
            <input id="to" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
          </div>
        </div>
        {notes.length === 0 ? (
          <p className="page-sub" style={{ marginBottom: 0 }}>
            No notes yet for this period.
          </p>
        ) : (
          <>
            <p className="page-sub" style={{ marginBottom: "0.5rem" }}>
              Edit inline, then click Update on each row to save.
            </p>
            <div className="table-wrap">
              <table className="data notes-table">
                <thead>
                  <tr>
                    <th>Date</th>
                    <th className="col-desc">Note</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {notes.map((n) => (
                    <tr key={n.id}>
                      <td>
                        <input
                          type="date"
                          value={n.noteDate}
                          onChange={(e) => updateNoteLocal(n.id, { noteDate: e.target.value })}
                        />
                      </td>
                      <td className="col-desc">
                        <textarea
                          className="desc-input"
                          value={n.body}
                          title={n.body}
                          rows={3}
                          onChange={(e) => updateNoteLocal(n.id, { body: e.target.value })}
                        />
                      </td>
                      <td>
                        <div style={{ display: "flex", flexDirection: "column", gap: "0.35rem" }}>
                          <button
                            type="button"
                            className="btn btn-ghost"
                            style={{ padding: "0.3rem 0.5rem", fontSize: "0.75rem" }}
                            disabled={savingNoteId === n.id}
                            onClick={() => saveNote(n)}
                          >
                            {savingNoteId === n.id ? "…" : "Update"}
                          </button>
                          <button
                            type="button"
                            className="btn btn-ghost"
                            style={{ padding: "0.3rem 0.6rem" }}
                            onClick={() => removeNote(n.id)}
                          >
                            ✕
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </div>

      <div className="card" style={{ marginBottom: "1rem" }}>
        <h2 style={{ margin: "0 0 0.35rem", fontSize: "1.1rem" }}>Summarize period</h2>
        <div style={{ display: "flex", flexWrap: "wrap", gap: "1.25rem", marginBottom: "0.75rem" }}>
          <label style={{ display: "flex", gap: "0.4rem", alignItems: "center" }}>
            <input
              type="checkbox"
              checked={includeNotes}
              onChange={(e) => setIncludeNotes(e.target.checked)}
            />
            Sticky notes
          </label>
          <label style={{ display: "flex", gap: "0.4rem", alignItems: "center" }}>
            <input
              type="checkbox"
              checked={includeGitlab}
              onChange={(e) => setIncludeGitlab(e.target.checked)}
            />
            GitLab
          </label>
          <label style={{ display: "flex", gap: "0.4rem", alignItems: "center" }}>
            <input
              type="checkbox"
              checked={includeMail}
              onChange={(e) => setIncludeMail(e.target.checked)}
            />
            Mail (IMAP)
          </label>
        </div>
        <p className="page-sub" style={{ marginTop: 0 }}>
          Configure GitLab / mail under Settings before enabling those sources.
        </p>
        <button className="btn" type="button" disabled={busy} onClick={draftFromSources}>
          {busy ? "Summarizing…" : "Summarize → worklog draft"}
        </button>
      </div>

      {rows.length > 0 && (
        <div className="card">
          <h2 style={{ margin: "0 0 0.35rem", fontSize: "1.1rem" }}>Preview</h2>
          <p className="page-sub" style={{ marginBottom: "0.5rem" }}>
            Edit anything before saving. Parser: {parser || "—"} · Sources: {sourcesUsed || "—"}
          </p>
          {preview && (
            <details style={{ marginBottom: "0.75rem" }}>
              <summary>Source digest preview</summary>
              <pre
                style={{
                  whiteSpace: "pre-wrap",
                  fontSize: "0.8rem",
                  maxHeight: 220,
                  overflow: "auto",
                }}
              >
                {preview}
              </pre>
            </details>
          )}
          <div className="table-wrap">
            <table className="data">
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Project</th>
                  <th>Activity</th>
                  <th>Hours</th>
                  <th className="col-desc">Description</th>
                  <th>Trust</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {rows.map((r, i) => (
                  <tr key={`${r.workDate}-${i}`}>
                    <td>
                      <input
                        type="date"
                        value={r.workDate}
                        onChange={(e) => updateRow(i, { workDate: e.target.value })}
                      />
                    </td>
                    <td>
                      <select
                        value={r.projectId || ""}
                        onChange={(e) => onProjectChange(i, e.target.value)}
                        title={r.projectName || undefined}
                      >
                        <option value="">Select…</option>
                        {projects.map((p) => (
                          <option key={p.id} value={p.id}>
                            {p.name}
                          </option>
                        ))}
                      </select>
                    </td>
                    <td>
                      <select
                        value={r.activityId || ""}
                        onChange={(e) => {
                          const project = projects.find((p) => p.id === r.projectId);
                          const act = project?.activities.find((a) => a.id === e.target.value);
                          updateRow(i, {
                            activityId: e.target.value,
                            activityName: act?.name || "",
                          });
                        }}
                      >
                        {(projects.find((p) => p.id === r.projectId)?.activities || []).map(
                          (a) => (
                            <option key={a.id} value={a.id}>
                              {a.name}
                            </option>
                          ),
                        )}
                      </select>
                    </td>
                    <td>
                      <input
                        type="number"
                        min={0.25}
                        max={24}
                        step={0.25}
                        value={r.hours}
                        onChange={(e) => updateRow(i, { hours: Number(e.target.value) })}
                      />
                    </td>
                    <td className="col-desc">
                      <textarea
                        className="desc-input"
                        value={r.description}
                        title={r.description}
                        rows={3}
                        onChange={(e) => updateRow(i, { description: e.target.value })}
                      />
                    </td>
                    <td>
                      <span className={r.confidence === "low" ? "chip warn" : "chip"}>
                        {r.confidence}
                      </span>
                    </td>
                    <td>
                      <button
                        type="button"
                        className="btn-ghost btn"
                        style={{ padding: "0.3rem 0.6rem" }}
                        onClick={() => setRows((prev) => prev.filter((_, idx) => idx !== i))}
                      >
                        ✕
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="btn-row">
            <button className="btn" type="button" onClick={saveRows} disabled={busy}>
              Save to worklogs
            </button>
            <button className="btn btn-ghost" type="button" onClick={exportToExcel} disabled={busy}>
              Export to Excel
            </button>
            <button className="btn btn-ghost" type="button" onClick={() => router.push("/compose")}>
              Open AI Entry
            </button>
            <button className="btn btn-ghost" type="button" onClick={() => router.push("/worklogs")}>
              View worklogs
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
