"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useMemo, useState } from "react";
import { getToken, gql } from "../../../lib/api";

type AutoTestCase = {
  id: string;
  name: string;
  category: string;
  surface: string;
  screen: string;
  operation: string;
  passed: boolean;
  description: string;
  detail: string | null;
  durationMs: number;
};

type ScreenSummary = {
  screen: string;
  surface: string;
  passed: boolean;
  totalTests: number;
  passedTests: number;
  detail: string | null;
};

type AutoTestReport = {
  runId: string;
  startedAt: string;
  completedAt: string;
  total: number;
  passed: number;
  failed: number;
  skipped: number;
  apiOnline: boolean;
  apiHealth: string | null;
  durationMs: number;
  tests: AutoTestCase[];
  webScreens: ScreenSummary[];
  windowsScreens: ScreenSummary[];
};

type FilterKey = "all" | "failed" | "api" | "web" | "windows" | "negative";

const RUN_QUERY = `query {
  runAutoTests {
    runId startedAt completedAt total passed failed skipped
    apiOnline apiHealth durationMs
    webScreens { screen surface passed totalTests passedTests detail }
    windowsScreens { screen surface passed totalTests passedTests detail }
    tests {
      id name category surface screen operation passed description detail durationMs
    }
  }
}`;

function formatWhen(iso: string) {
  try {
    return new Date(iso).toLocaleString(undefined, {
      dateStyle: "medium",
      timeStyle: "short",
    });
  } catch {
    return iso;
  }
}

function passRate(passed: number, total: number) {
  if (total === 0) return 0;
  return Math.round((passed / total) * 100);
}

export default function AutoTestPage() {
  const router = useRouter();
  const [report, setReport] = useState<AutoTestReport | null>(null);
  const [running, setRunning] = useState(false);
  const [error, setError] = useState("");
  const [filter, setFilter] = useState<FilterKey>("all");
  const [expanded, setExpanded] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<"overview" | "api" | "web" | "windows" | "details">(
    "overview",
  );

  const runTests = useCallback(async () => {
    const token = getToken();
    if (!token) {
      router.replace("/login");
      return;
    }
    setRunning(true);
    setError("");
    try {
      const data = await gql<{ runAutoTests: AutoTestReport }>(RUN_QUERY, undefined, token);
      setReport(data.runAutoTests);
      setExpanded(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Test run failed");
    } finally {
      setRunning(false);
    }
  }, [router]);

  useEffect(() => {
    const token = getToken();
    if (!token) {
      router.replace("/login");
    }
  }, [router]);

  const filteredTests = useMemo(() => {
    if (!report) return [];
    return report.tests.filter((t) => {
      switch (filter) {
        case "failed":
          return !t.passed;
        case "api":
          return t.surface === "API";
        case "web":
          return t.surface === "Web";
        case "windows":
          return t.surface === "Windows";
        case "negative":
          return t.operation === "Negative";
        default:
          return true;
      }
    });
  }, [report, filter]);

  const apiTests = useMemo(
    () => report?.tests.filter((t) => t.surface === "API") ?? [],
    [report],
  );

  const categories = useMemo(() => {
    const set = new Set(apiTests.map((t) => t.category));
    return Array.from(set).sort();
  }, [apiTests]);

  return (
    <div className="autotest-page">
      <div className="autotest-header">
        <div>
          <nav className="autotest-breadcrumb">
            <Link href="/admin">Admin</Link>
            <span aria-hidden="true">/</span>
            <span>Auto Test</span>
          </nav>
          <h1 className="page-title">Auto Test</h1>
          <p className="page-sub" style={{ marginBottom: 0 }}>
            End-to-end health check for LogEase API, web screens, and Windows tray app
            dependencies. Covers CRUD flows, validation (negative tests), and integrations.
          </p>
        </div>
        <button
          type="button"
          className="btn autotest-run-btn"
          disabled={running}
          onClick={runTests}
        >
          {running ? (
            <>
              <span className="autotest-spinner" aria-hidden="true" />
              Running tests…
            </>
          ) : (
            "Run all tests"
          )}
        </button>
      </div>

      {error && <p className="error">{error}</p>}

      {!report && !running && !error && (
        <div className="card autotest-empty">
          <p>No test run yet. Click <strong>Run all tests</strong> to verify API status and every screen.</p>
        </div>
      )}

      {report && (
        <>
          <div className="autotest-summary-grid">
            <div className={`autotest-stat ${report.apiOnline ? "ok" : "fail"}`}>
              <span className="autotest-stat-label">API status</span>
              <span className="autotest-stat-value">
                {report.apiOnline ? "Online" : "Offline"}
              </span>
              <span className="autotest-stat-sub">{report.apiHealth ?? "—"}</span>
            </div>
            <div className={`autotest-stat ${report.failed === 0 ? "ok" : "fail"}`}>
              <span className="autotest-stat-label">Overall</span>
              <span className="autotest-stat-value">
                {report.passed}/{report.total} passed
              </span>
              <span className="autotest-stat-sub">{passRate(report.passed, report.total)}% pass rate</span>
            </div>
            <div className="autotest-stat">
              <span className="autotest-stat-label">Duration</span>
              <span className="autotest-stat-value">{(report.durationMs / 1000).toFixed(1)}s</span>
              <span className="autotest-stat-sub">Run {report.runId}</span>
            </div>
            <div className="autotest-stat">
              <span className="autotest-stat-label">Last run</span>
              <span className="autotest-stat-value">{formatWhen(report.completedAt)}</span>
              <span className="autotest-stat-sub">
                {report.failed > 0 ? `${report.failed} failed` : "All green"}
              </span>
            </div>
          </div>

          <div className="autotest-tabs" role="tablist">
            {(
              [
                ["overview", "Overview"],
                ["api", "API & CRUD"],
                ["web", "Web app"],
                ["windows", "Windows app"],
                ["details", "All results"],
              ] as const
            ).map(([key, label]) => (
              <button
                key={key}
                type="button"
                role="tab"
                aria-selected={activeTab === key}
                className={activeTab === key ? "autotest-tab active" : "autotest-tab"}
                onClick={() => setActiveTab(key)}
              >
                {label}
              </button>
            ))}
          </div>

          {activeTab === "overview" && (
            <div className="autotest-panels">
              <section className="card autotest-panel">
                <h2>Quick status</h2>
                <div className="autotest-chip-row">
                  <span className={`chip ${report.failed === 0 ? "" : "warn"}`}>
                    {report.failed === 0 ? "All tests passed" : `${report.failed} failure(s)`}
                  </span>
                  <span className="chip">
                    {apiTests.filter((t) => t.operation === "Negative").length} negative tests
                  </span>
                  <span className="chip">{report.webScreens.length} web screens</span>
                  <span className="chip">{report.windowsScreens.length} Windows dialogs</span>
                </div>
                <p className="page-sub">
                  Categories covered: {categories.join(", ")}.
                </p>
              </section>
            </div>
          )}

          {activeTab === "api" && (
            <div className="autotest-panels">
              {categories.map((cat) => {
                const tests = apiTests.filter((t) => t.category === cat);
                const catPassed = tests.filter((t) => t.passed).length;
                return (
                  <section key={cat} className="card autotest-panel">
                    <div className="autotest-panel-head">
                      <h2>{cat}</h2>
                      <span className={catPassed === tests.length ? "chip" : "chip warn"}>
                        {catPassed}/{tests.length}
                      </span>
                    </div>
                    <ul className="autotest-test-list">
                      {tests.map((t) => (
                        <li key={t.id} className={t.passed ? "pass" : "fail"}>
                          <button
                            type="button"
                            className="autotest-test-row"
                            onClick={() => setExpanded(expanded === t.id ? null : t.id)}
                          >
                            <span className="autotest-badge">{t.passed ? "PASS" : "FAIL"}</span>
                            <span className="autotest-test-name">{t.name}</span>
                            <span className="autotest-op">{t.operation}</span>
                            <span className="autotest-ms">{t.durationMs}ms</span>
                          </button>
                          {(expanded === t.id || !t.passed) && (
                            <div className="autotest-detail">
                              <p>{t.description}</p>
                              {t.detail && <p className="autotest-detail-msg">{t.detail}</p>}
                            </div>
                          )}
                        </li>
                      ))}
                    </ul>
                  </section>
                );
              })}
            </div>
          )}

          {activeTab === "web" && (
            <ScreenGrid
              title="Web application screens"
              screens={report.webScreens}
              onRun={runTests}
              running={running}
            />
          )}

          {activeTab === "windows" && (
            <ScreenGrid
              title="Windows tray application"
              screens={report.windowsScreens}
              onRun={runTests}
              running={running}
            />
          )}

          {activeTab === "details" && (
            <div className="card autotest-panel">
              <div className="autotest-filter-row">
                {(
                  [
                    ["all", "All"],
                    ["failed", "Failed only"],
                    ["api", "API"],
                    ["web", "Web"],
                    ["windows", "Windows"],
                    ["negative", "Negative"],
                  ] as const
                ).map(([key, label]) => (
                  <button
                    key={key}
                    type="button"
                    className={filter === key ? "autotest-filter active" : "autotest-filter"}
                    onClick={() => setFilter(key)}
                  >
                    {label}
                  </button>
                ))}
              </div>
              <div className="table-wrap">
                <table className="data autotest-table">
                  <thead>
                    <tr>
                      <th>Status</th>
                      <th>Test</th>
                      <th>Surface</th>
                      <th>Operation</th>
                      <th>Time</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredTests.length === 0 ? (
                      <tr>
                        <td colSpan={5} style={{ color: "var(--muted)" }}>
                          No tests match this filter.
                        </td>
                      </tr>
                    ) : (
                      filteredTests.map((t) => (
                        <tr key={t.id} className={t.passed ? "row-pass" : "row-fail"}>
                          <td>
                            <span className={t.passed ? "chip" : "chip warn"}>
                              {t.passed ? "Pass" : "Fail"}
                            </span>
                          </td>
                          <td>
                            <strong>{t.name}</strong>
                            <p className="autotest-table-desc">{t.description}</p>
                            {t.detail && (
                              <p className="autotest-table-detail">{t.detail}</p>
                            )}
                          </td>
                          <td>
                            {t.surface}
                            <br />
                            <small style={{ color: "var(--muted)" }}>{t.screen}</small>
                          </td>
                          <td>{t.operation}</td>
                          <td>{t.durationMs}ms</td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </>
      )}
    </div>
  );
}

function ScreenGrid({
  title,
  screens,
  onRun,
  running,
}: {
  title: string;
  screens: ScreenSummary[];
  onRun: () => void;
  running: boolean;
}) {
  return (
    <div>
      <div className="autotest-screen-toolbar">
        <h2 className="autotest-screen-title">{title}</h2>
        <button type="button" className="btn btn-ghost" disabled={running} onClick={onRun}>
          Re-run tests
        </button>
      </div>
      <div className="autotest-screen-grid">
        {screens.map((s) => (
          <article
            key={s.screen}
            className={`card autotest-screen-card ${s.passed ? "pass" : "fail"}`}
          >
            <div className="autotest-screen-card-head">
              <span className={s.passed ? "autotest-badge pass" : "autotest-badge fail"}>
                {s.passed ? "PASS" : "FAIL"}
              </span>
              <h3>{s.screen}</h3>
            </div>
            <p className="autotest-screen-meta">
              CRUD / API coverage: {s.passedTests}/{s.totalTests} checks passed
            </p>
            {s.detail && <p className="autotest-screen-detail">{s.detail}</p>}
            <div className="autotest-progress">
              <div
                className="autotest-progress-bar"
                style={{ width: `${passRate(s.passedTests, s.totalTests)}%` }}
              />
            </div>
          </article>
        ))}
      </div>
    </div>
  );
}
