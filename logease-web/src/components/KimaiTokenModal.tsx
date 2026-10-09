"use client";

import { FormEvent, useState } from "react";
import { gql } from "../lib/api";

type Props = {
  token: string;
  onDone: () => void;
  onSkip: () => void;
};

export function KimaiTokenModal({ token, onDone, onSkip }: Props) {
  const [kimaiToken, setKimaiToken] = useState("");
  const [error, setError] = useState("");
  const [ok, setOk] = useState("");
  const [loading, setLoading] = useState(false);

  async function onSave(e: FormEvent) {
    e.preventDefault();
    if (!kimaiToken.trim()) {
      onSkip();
      return;
    }

    setLoading(true);
    setError("");
    setOk("");
    try {
      await gql(
        `mutation ($input: UpdateKimaiSettingsInput!) {
          updateKimaiSettings(input: $input) {
            hasKimaiToken kimaiError
            kimaiUser { displayName email }
          }
        }`,
        { input: { apiToken: kimaiToken.trim() } },
        token,
      );
      setOk("Kimai connected — your office account is linked to your Kimai profile.");
      setTimeout(onDone, 700);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not save Kimai token");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="modal-backdrop" role="dialog" aria-modal="true" aria-labelledby="kimai-title">
      <div className="modal-card kimai-modal">
        <h2 id="kimai-title">Connect Kimai (optional)</h2>
        <p className="page-sub" style={{ marginBottom: "1rem" }}>
          Paste your Kimai API token to sync projects and save worklogs. You can add this later in
          Settings.
        </p>
        <form onSubmit={onSave}>
          <div className="field">
            <label htmlFor="kimai-token">Kimai API token</label>
            <input
              id="kimai-token"
              type="password"
              value={kimaiToken}
              onChange={(e) => setKimaiToken(e.target.value)}
              placeholder="From Kimai → Profile → API Access"
              autoComplete="off"
            />
          </div>
          {error && <p className="error">{error}</p>}
          {ok && <p className="ok">{ok}</p>}
          <div className="btn-row">
            <button className="btn" type="submit" disabled={loading}>
              {loading ? "Connecting…" : "Connect Kimai"}
            </button>
            <button type="button" className="btn btn-ghost" disabled={loading} onClick={onSkip}>
              Skip for now
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
