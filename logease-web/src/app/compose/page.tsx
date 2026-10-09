"use client";

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { ReminderBanner } from "../../components/ReminderBanner";
import { getToken, gql } from "../../lib/api";

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

export default function ComposePage() {
  const router = useRouter();
  const initial = useMemo(() => weekRange(), []);
  const [text, setText] = useState(
    "Monday to Friday last 2 weeks I worked on Perception Survey AI — debugging analytics, survey question regeneration, survey prompts etc. Split across those two weeks properly.\n\nOther project is Company Announcement — I worked on debugging to find duplicate data on the translate table.",
  );
  const [dateFrom, setDateFrom] = useState(initial.from);
  const [dateTo, setDateTo] = useState(initial.to);
  const [projects, setProjects] = useState<Project[]>([]);
  const [rows, setRows] = useState<ParsedRow[]>([]);
  const [parser, setParser] = useState("");
  const [error, setError] = useState("");
  const [ok, setOk] = useState("");
  const [busy, setBusy] = useState(false);
  const [customizeOpen, setCustomizeOpen] = useState(false);
  const [aiDraft, setAiDraft] = useState("");
  const [aiBusy, setAiBusy] = useState(false);
  const [defaultAi, setDefaultAi] = useState("");

  async function loadProjects(token: string) {
    const d = await gql<{ projects: Project[] }>(
      `query { projects { id name activities { id name projectId } } }`,
      undefined,
      token,
    );
    setProjects(d.projects);
  }

  useEffect(() => {
    const token = getToken();
    if (!token) {
      router.replace("/login");
      return;
    }
    loadProjects(token).catch((e) => setError(e.message));
  }, [router]);

  async function generate() {
    const token = getToken();
    if (!token) return;
    setBusy(true);
    setError("");
    setOk("");
    try {
      const data = await gql<{
        parseWorklog: { rows: ParsedRow[]; parser: string; warning?: string | null };
      }>(
        `mutation ($input: ParseWorklogInput!) {
          parseWorklog(input: $input) {
            parser
            warning
            rows {
              workDate projectName activityName hours description confidence projectId activityId
            }
          }
        }`,
        { input: { text, dateFrom, dateTo } },
        token,
      );
      setRows(data.parseWorklog.rows);
      setParser(data.parseWorklog.parser);
      // New project names from the prompt are auto-created — refresh dropdown
      await loadProjects(token);
      if (data.parseWorklog.parser === "demo-heuristic") {
        setOk(
          data.parseWorklog.warning
            ? `Used offline parser. AI failed: ${data.parseWorklog.warning}`
            : "Used offline parser (AI key empty or call failed).",
        );
      } else if (data.parseWorklog.parser === "openrouter" || data.parseWorklog.parser === "gemini") {
        setOk(`AI table generated via ${data.parseWorklog.parser}.`);
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "Parse failed");
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

  function clearAll() {
    if (
      !window.confirm(
        "Clear the prompt text and the preview table?\n\nThis does not delete worklogs already saved on the server/Kimai.",
      )
    ) {
      return;
    }
    const range = weekRange();
    setText("");
    setRows([]);
    setParser("");
    setError("");
    setOk("Cleared — ready for a new prompt.");
    setDateFrom(range.from);
    setDateTo(range.to);
  }

  function openCustomize() {
    const token = getToken();
    if (!token) return;
    setCustomizeOpen(true);
    setAiBusy(true);
    Promise.all([
      gql<{ me: { aiParseInstructions?: string | null } }>(
        `query { me { aiParseInstructions } }`,
        undefined,
        token,
      ),
      gql<{ defaultAiParseInstructions: string }>(
        `query { defaultAiParseInstructions }`,
        undefined,
        token,
      ),
    ])
      .then(([meRes, defRes]) => {
        setDefaultAi(defRes.defaultAiParseInstructions || "");
        setAiDraft(meRes.me?.aiParseInstructions?.trim() || defRes.defaultAiParseInstructions || "");
      })
      .catch((e) => setError(e instanceof Error ? e.message : "Failed to load AI wording"))
      .finally(() => setAiBusy(false));
  }

  async function saveAiWording() {
    const token = getToken();
    if (!token) return;
    setAiBusy(true);
    setError("");
    try {
      await gql(
        `mutation ($input: UpdateAiParseInstructionsInput!) {
          updateAiParseInstructions(input: $input) { aiParseInstructions }
        }`,
        { input: { instructions: aiDraft } },
        token,
      );
      setCustomizeOpen(false);
      setOk("AI wording saved. Generate table will use it for free-text notes.");
    } catch (e) {
      setError(e instanceof Error ? e.message : "Save failed");
    } finally {
      setAiBusy(false);
    }
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
    a.download = `logease-worklogs-${new Date().toISOString().slice(0, 10)}.csv`;
    a.click();
    URL.revokeObjectURL(url);
    setOk("Exported to Excel-compatible CSV.");
  }

  async function save() {
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
      const data = await gql<{ saveWorklogs: { savedCount: number; destination: string; message?: string | null } }>(
        `mutation ($input: SaveWorklogsInput!) {
          saveWorklogs(input: $input) { savedCount destination message }
        }`,
        { input: { items } },
        token,
      );
      const destinationLabel =
        data.saveWorklogs.destination === "Kimai"
          ? "Kimai"
          : data.saveWorklogs.destination === "Both"
            ? "local server and Kimai"
            : "local server";
      setOk(
        data.saveWorklogs.message ??
          `Saved ${data.saveWorklogs.savedCount} worklog(s) to ${destinationLabel}.`,
      );
      setRows([]);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Save failed");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div>
      <ReminderBanner />
      <h1 className="page-title">AI Entry</h1>
      <p className="page-sub">
        Write freely — any style is fine (typos, “June”, “last 2 weeks”). AI builds the table from your
        words (dates, projects, hours). Offline logic is used only if AI fails. Edit, then save.
      </p>

      <div className="card">
        <div className="row">
          <div className="field">
            <label htmlFor="from">From (used if prompt has no dates)</label>
            <input
              id="from"
              type="date"
              value={dateFrom}
              onChange={(e) => setDateFrom(e.target.value)}
            />
          </div>
          <div className="field">
            <label htmlFor="to">To (used if prompt has no dates)</label>
            <input
              id="to"
              type="date"
              value={dateTo}
              onChange={(e) => setDateTo(e.target.value)}
            />
          </div>
        </div>
        <p className="page-sub" style={{ marginTop: "-0.35rem", marginBottom: "0.75rem", fontSize: "0.85rem" }}>
          From/To control the table dates unless your prompt names a period (e.g. “only June” / “last 2 weeks”).
        </p>
        <div className="field">
          <label htmlFor="text">What did you work on?</label>
          <textarea
            id="text"
            className="prompt-input"
            value={text}
            onChange={(e) => setText(e.target.value)}
            rows={12}
            placeholder={`Example:\nMonday to Friday I worked on Perception Survey AI — analytics debug, question regeneration, prompts. Split for June.\nOther project is Company Announcement — debugging duplicate data on translate table. Only for June.`}
          />
          <p className="page-sub" style={{ marginTop: "0.35rem", marginBottom: 0, fontSize: "0.8rem" }}>
            Drag the bottom-right corner to resize if the prompt is long. Use Customize AI wording to teach how your team writes notes.
          </p>
        </div>
        <div className="btn-row">
          <button className="btn" type="button" onClick={generate} disabled={busy}>
            {busy ? "Working…" : "Generate table"}
          </button>
          <button className="btn btn-ghost" type="button" onClick={openCustomize} disabled={busy}>
            Customize AI wording
          </button>
          <button className="btn btn-ghost" type="button" onClick={clearAll} disabled={busy}>
            Clear all
          </button>
          {parser && (
            <span className="chip" title="Parser used">
              parser: {parser}
            </span>
          )}
        </div>
        {error && <p className="error">{error}</p>}
        {ok && <p className="ok">{ok}</p>}
      </div>

      {customizeOpen && (
        <div
          className="modal-backdrop"
          role="dialog"
          aria-modal="true"
          aria-label="Customize AI wording"
          onClick={() => setCustomizeOpen(false)}
        >
          <div className="modal-card" onClick={(e) => e.stopPropagation()}>
            <h2>Customize AI wording</h2>
            <p className="page-sub" style={{ marginTop: 0 }}>
              Teach the AI how your team writes free text (typos, nicknames, “other project is…”).
              This is not a worklog template — it shapes how Generate maps any format into LogEase rows.
            </p>
            <textarea
              className="prompt-input"
              value={aiDraft}
              onChange={(e) => setAiDraft(e.target.value)}
              rows={16}
              disabled={aiBusy}
            />
            <div className="btn-row" style={{ marginTop: "0.85rem" }}>
              <button className="btn" type="button" onClick={saveAiWording} disabled={aiBusy}>
                {aiBusy ? "Saving…" : "Save for my account"}
              </button>
              <button
                className="btn btn-ghost"
                type="button"
                disabled={aiBusy || !defaultAi}
                onClick={() => setAiDraft(defaultAi)}
              >
                Reset to default
              </button>
              <button className="btn btn-ghost" type="button" onClick={() => setCustomizeOpen(false)}>
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}

      {rows.length > 0 && (
        <div className="card" style={{ marginTop: "1rem" }}>
          <h2 style={{ margin: "0 0 0.35rem", fontSize: "1.1rem" }}>Preview</h2>
          <p className="page-sub" style={{ marginBottom: "0.5rem" }}>
            Edit anything before saving. This is human-in-the-loop.
          </p>
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
            <button className="btn" type="button" onClick={save} disabled={busy}>
              Save to worklogs
            </button>
            <button className="btn btn-ghost" type="button" onClick={exportToExcel} disabled={busy}>
              Export to Excel
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
