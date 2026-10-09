"use client";

import Link from "next/link";
import { FormEvent, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { getToken, gql } from "../../lib/api";

type Project = {
  id: string;
  name: string;
  isActive: boolean;
  activities: { id: string; name: string }[];
};

type AppSettings = {
  worklogSaveTarget: string;
  worklogSaveTargetLabel: string;
  kimaiBaseUrlConfigured: boolean;
  kimaiBaseUrl?: string | null;
};

const SAVE_OPTIONS = [
  {
    value: "LocalServer",
    label: "Local server only",
    help: "Save worklogs only in the LogEase database (SQL Server).",
  },
  {
    value: "Kimai",
    label: "Kimai only",
    help: "Post worklogs directly to Kimai. Nothing is stored as a worklog row in LogEase.",
  },
  {
    value: "Both",
    label: "Local server and Kimai",
    help: "Save a copy in LogEase and also post the same entries to Kimai.",
  },
] as const;

export default function AdminPage() {
  const router = useRouter();
  const [projects, setProjects] = useState<Project[]>([]);
  const [name, setName] = useState("");
  const [saveTarget, setSaveTarget] = useState("LocalServer");
  const [appSettings, setAppSettings] = useState<AppSettings | null>(null);
  const [destMsg, setDestMsg] = useState("");
  const [destError, setDestError] = useState("");
  const [projectMsg, setProjectMsg] = useState("");
  const [projectError, setProjectError] = useState("");
  const [busy, setBusy] = useState(false);

  async function load(token: string) {
    const projectsData = await gql<{ projects: Project[] }>(
      `query { projects(includeInactive: true) { id name isActive activities { id name } } }`,
      undefined,
      token,
    );
    setProjects(projectsData.projects);

    try {
      const settingsData = await gql<{ appSettings: AppSettings }>(
        `query {
          appSettings {
            worklogSaveTarget
            worklogSaveTargetLabel
            kimaiBaseUrlConfigured
            kimaiBaseUrl
          }
        }`,
        undefined,
        token,
      );
      setAppSettings(settingsData.appSettings);
      setSaveTarget(settingsData.appSettings.worklogSaveTarget);
      setDestError("");
    } catch (e) {
      setDestError(
        e instanceof Error
          ? `Could not load save destination: ${e.message}. Restart LogEase.Api and create table logease_AppSettings if needed.`
          : "Could not load save destination settings.",
      );
    }
  }

  useEffect(() => {
    const token = getToken();
    if (!token) {
      router.replace("/login");
      return;
    }
    load(token).catch((e) => setProjectError(e.message));
  }, [router]);

  async function onAdd(e: FormEvent) {
    e.preventDefault();
    const token = getToken();
    if (!token || !name.trim()) return;
    setBusy(true);
    setProjectError("");
    setProjectMsg("");
    try {
      const d = await gql<{ createProject: Project }>(
        `mutation ($input: CreateProjectInput!) {
          createProject(input: $input) { id name isActive activities { id name } }
        }`,
        { input: { name: name.trim() } },
        token,
      );
      setProjectMsg(
        `Saved “${d.createProject.name}”. Close spelling matches reuse the existing project instead of creating a duplicate.`,
      );
      setName("");
      await load(token);
    } catch (err) {
      setProjectError(err instanceof Error ? err.message : "Failed to add project");
    } finally {
      setBusy(false);
    }
  }

  async function onSaveTarget(e: FormEvent) {
    e.preventDefault();
    const token = getToken();
    if (!token) {
      setDestError("You are not logged in. Open Login and try again.");
      return;
    }
    setBusy(true);
    setDestError("");
    setDestMsg("");
    try {
      const d = await gql<{ updateAppSettings: AppSettings }>(
        `mutation ($input: UpdateAppSettingsInput!) {
          updateAppSettings(input: $input) {
            worklogSaveTarget
            worklogSaveTargetLabel
            kimaiBaseUrlConfigured
            kimaiBaseUrl
          }
        }`,
        { input: { worklogSaveTarget: saveTarget } },
        token,
      );
      if (!d?.updateAppSettings) {
        throw new Error("API returned no result. Restart LogEase.Api with the latest build.");
      }
      setAppSettings(d.updateAppSettings);
      setSaveTarget(d.updateAppSettings.worklogSaveTarget);
      setDestMsg(
        `Saved. Worklog save destination is now “${d.updateAppSettings.worklogSaveTargetLabel}”.`,
      );
    } catch (err) {
      setDestError(err instanceof Error ? err.message : "Failed to update save destination");
    } finally {
      setBusy(false);
    }
  }

  async function toggleActive(p: Project) {
    const token = getToken();
    if (!token) return;
    setBusy(true);
    setProjectError("");
    try {
      await gql(
        `mutation ($input: SetProjectActiveInput!) {
          setProjectActive(input: $input) { id isActive }
        }`,
        { input: { projectId: p.id, isActive: !p.isActive } },
        token,
      );
      await load(token);
    } catch (err) {
      setProjectError(err instanceof Error ? err.message : "Update failed");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div>
      <h1 className="page-title">Admin</h1>
      <p className="page-sub">
        Office-wide settings and the project catalog. Employees still add their own Kimai
        API token under Settings when Kimai posting is enabled.
      </p>

      <div className="card" style={{ marginBottom: "1rem", maxWidth: 640 }}>
        <h2 style={{ marginTop: 0 }}>Auto Test</h2>
        <p className="page-sub" style={{ marginTop: 0 }}>
          Run automated health checks across API, web screens, and Windows app dependencies.
          Covers CRUD operations, negative validation, and integration status.
        </p>
        <Link href="/admin/auto-test" className="btn" style={{ textDecoration: "none" }}>
          Open Auto Test dashboard
        </Link>
      </div>

      <div className="card" style={{ marginBottom: "1rem", maxWidth: 640 }}>
        <h2 style={{ marginTop: 0 }}>Worklog save destination</h2>
        <p className="page-sub" style={{ marginTop: 0 }}>
          Controls where Save sends worklogs for everyone. Default can also be set in
          <code> appsettings.json </code>
          under <code>WorklogStorage:SaveTarget</code>. Admin changes here override that default.
        </p>
        <form onSubmit={onSaveTarget}>
          <div className="field">
            <label htmlFor="saveTarget">Save destination</label>
            <select
              id="saveTarget"
              value={saveTarget}
              onChange={(e) => setSaveTarget(e.target.value)}
            >
              {SAVE_OPTIONS.map((opt) => (
                <option key={opt.value} value={opt.value}>
                  {opt.label}
                </option>
              ))}
            </select>
          </div>
          <p className="page-sub" style={{ marginTop: 0 }}>
            {SAVE_OPTIONS.find((o) => o.value === saveTarget)?.help}
          </p>
          {appSettings && (
            <p className="page-sub" style={{ marginTop: 0 }}>
              Kimai base URL:{" "}
              {appSettings.kimaiBaseUrlConfigured
                ? appSettings.kimaiBaseUrl
                : "not configured — set Kimai:BaseUrl in appsettings first"}
            </p>
          )}
          {destMsg && <p className="ok">{destMsg}</p>}
          {destError && <p className="error">{destError}</p>}
          <button className="btn" type="submit" disabled={busy}>
            {busy ? "Saving…" : "Save destination"}
          </button>
        </form>
      </div>

      <div className="card" style={{ marginBottom: "1rem" }}>
        <h2 style={{ marginTop: 0 }}>Projects</h2>
        <p className="page-sub" style={{ marginTop: 0 }}>
          Maintain the project catalog. When someone types a near-miss name (e.g. “Perception Survery”),
          LogEase fuzzy-matches it to the closest project here instead of creating a duplicate.
        </p>
        <form onSubmit={onAdd}>
          <div className="field">
            <label htmlFor="pname">Add project</label>
            <input
              id="pname"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g. Perception Survey"
              required
            />
          </div>
          {projectMsg && <p className="ok">{projectMsg}</p>}
          {projectError && <p className="error">{projectError}</p>}
          <div className="btn-row">
            <button className="btn" type="submit" disabled={busy}>
              {busy ? "Saving…" : "Add project"}
            </button>
          </div>
        </form>
      </div>

      <div className="card">
        <div className="table-wrap">
          <table className="data">
            <thead>
              <tr>
                <th>Project</th>
                <th>Activities</th>
                <th>Status</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {projects.length === 0 ? (
                <tr>
                  <td colSpan={4} style={{ color: "var(--muted)" }}>
                    No projects yet.
                  </td>
                </tr>
              ) : (
                projects.map((p) => (
                  <tr key={p.id}>
                    <td>{p.name}</td>
                    <td style={{ color: "var(--muted)", fontSize: "0.85rem" }}>
                      {p.activities.map((a) => a.name).join(", ")}
                    </td>
                    <td>
                      <span className={p.isActive ? "chip" : "chip warn"}>
                        {p.isActive ? "Active" : "Inactive"}
                      </span>
                    </td>
                    <td>
                      <button
                        type="button"
                        className="btn btn-ghost"
                        style={{ padding: "0.3rem 0.75rem" }}
                        disabled={busy}
                        onClick={() => toggleActive(p)}
                      >
                        {p.isActive ? "Deactivate" : "Activate"}
                      </button>
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
