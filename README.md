# LogEase

Worklog Copilot: free-text → structured worklogs → SQL Server (`logease_*` tables).  
Kimai sync: stub only (not used in demo).

## Database setup (do this first)

1. Open SSMS / Azure Data Studio against your **test** DB (SQL Server).
2. Run: **`Scripts/SqlServer_All.sql`**
3. Creates tables: `logease_Users`, `logease_Projects`, `logease_Activities`, `logease_Worklogs` + demo seed.

Demo login: `demo@logease.local` / `Demo@123`

API uses your connection string in `LogEase.Api/appsettings.json`  
(`Database:UseEnsureCreated` is **false** — prefer the SQL script on shared DBs).

## Run

```bash
cd LogEase.Api
dotnet run

cd logease-web
npm run dev
```

### Build API (safe against running API lock)
If the API is currently running and you get a Windows file-lock error during build:
```powershell
cd LogEase
.\Scripts\build-api.ps1
```

- API GraphQL: http://localhost:5055/graphql  
- Web: http://localhost:3000  

## Windows tray app (alert + free-text dialog)

### Why `dotnet run` looks like it “does nothing”

`LogEase.Desktop` is a **WinExe tray app** — it intentionally has **no console window**. After `dotnet run`, look near the **system clock / tray** for the LogEase icon (and a balloon: “LogEase is running”). Right-click → **Log work…** / **Settings…**.

### Install a Desktop shortcut (.exe)

```powershell
cd LogEase\Scripts
.\Publish-Desktop.ps1
```

- Builds `publish\LogEase.Desktop\LogEase.Desktop.exe`
- Creates a **LogEase** shortcut on your Desktop
- Needs **.NET 8 Runtime** installed, unless you use: `.\Publish-Desktop.ps1 -SelfContained`
- **API must still be running** (`cd LogEase.Api && dotnet run`) when you log work

Manual run without publish:

```bash
cd LogEase.Desktop
dotnet run
```

- At alert time shows a Windows balloon reminder
- Double-click tray or **Log work…** → free text → Generate → Save
- Right-click tray → Settings / Exit

## Structure

```
LogEase/
  LogEase.Api/
  logease-web/
  LogEase.Desktop/            ← Windows tray tool
  mcp/                        ← MCP server (Cursor talks to LogEase)
  Scripts/SqlServer_All.sql   ← all DB scripts
  docs/IMPL.md
  docs/MCP_FOR_LOGEASE.md     ← MCP in simple words
  README.md
```

## MCP (Cursor → LogEase)

Talk to LogEase from Cursor chat (parse notes, list projects, save with confirm).

1. Start API (`dotnet run` in `LogEase.Api`)
2. `cd mcp && npm install && npm run build`
3. Project MCP config is at `.cursor/mcp.json` (demo login). Reload MCP in Cursor.
4. Read: [docs/MCP_FOR_LOGEASE.md](docs/MCP_FOR_LOGEASE.md) and [mcp/README.md](mcp/README.md)

## Sticky notes + GitLab / mail (Phase A / B)

Daily sticky notes (web + tray) can be summarized into a worklog draft. Optionally include GitLab events and IMAP mail.

See [docs/PHASE_A_B_NOTES_SOURCES.md](docs/PHASE_A_B_NOTES_SOURCES.md).

## Worklog save destination

Office-wide setting (not per prompt). Choose where Save sends worklogs:

| Value | Meaning |
|-------|---------|
| `LocalServer` | Save only to LogEase SQL Server |
| `Kimai` | Post only to Kimai |
| `Both` | Save to LogEase and post to Kimai |

Set default in `LogEase.Api/appsettings.json`:

```json
"WorklogStorage": {
  "SaveTarget": "LocalServer"
}
```

Also set the Kimai URL once:

```json
"Kimai": {
  "BaseUrl": "https://kimai.yourcompany.com"
}
```

Admins can change the destination in **Admin → Worklog save destination** (overrides appsettings).
Employees still paste their own Kimai API token in **Settings** when Kimai is included.

## Parsing flow

1. **AI first** — free text → JSON worklog rows (AI chooses June / 2 weeks / etc.; UI From/To are optional hints).
2. **Heuristic fallback** — only if the AI key is missing or the call fails (old period-expansion logic).

## AI parsing

LogEase runs a free local Qwen model through Ollama first, then uses optional
OpenAI, OpenRouter, or Gemini fallbacks before the offline heuristic parser.
Configure models and the provider order in `LogEase.Api/appsettings.json`.

See [AI model setup and session explanation](docs/LLM_PROVIDER_SETUP.md) for
Ollama commands, secure API-key configuration, efficiency checks, and the
short explanation to use in a demo session.

## Security note

Prefer User Secrets / env vars for DB passwords in real repos — avoid committing secrets.
