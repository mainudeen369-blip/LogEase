# LogEase MCP server

Let **Cursor** call LogEase tools over MCP (same GraphQL API as web + desktop).

Plain-English overview: [docs/MCP_FOR_LOGEASE.md](../docs/MCP_FOR_LOGEASE.md)

## Prerequisites

1. **LogEase.Api** running: `cd LogEase.Api && dotnet run`  
   GraphQL: `http://localhost:5055/graphql`
2. Node.js **18+**
3. A LogEase user (demo: `demo@logease.local` / `Demo@123`)

## Install

```powershell
cd C:\HajaWorkingFolder\LogEase\mcp
npm install
npm run build
```

## Wire into Cursor

1. Open Cursor Settings → **MCP** (or edit your MCP JSON).
2. Merge the block from [`cursor-mcp.example.json`](cursor-mcp.example.json).
3. Set `LOGEASE_EMAIL` / `LOGEASE_PASSWORD` to your account.
4. Fix the `args` path if your repo lives somewhere else.
5. Reload MCP / restart Cursor.
6. Confirm the **logease** server shows green / connected.

### Example config

```json
{
  "mcpServers": {
    "logease": {
      "command": "node",
      "args": ["C:/HajaWorkingFolder/LogEase/mcp/dist/index.js"],
      "env": {
        "LOGEASE_API_URL": "http://localhost:5055/graphql",
        "LOGEASE_EMAIL": "demo@logease.local",
        "LOGEASE_PASSWORD": "Demo@123"
      }
    }
  }
}
```

## Tools

| Tool | Purpose |
|------|---------|
| `whoami` | Current user + save destination |
| `list_projects` | Projects / activities |
| `parse_worklog` | Free text → draft rows (LogEase AI) |
| `list_worklogs` | Saved rows in a date range |
| `list_sticky_notes` | List daily sticky notes (optional date range) |
| `create_sticky_note` | Add a daily sticky note |
| `draft_worklog_from_sources` | Notes ± GitLab ± mail → draft rows |
| `save_worklogs` | Save rows (`confirm: true` required) |

## Try it in Cursor chat

```text
Using the LogEase MCP tools, parse this for 2026-07-14 to 2026-07-18:

Mon-Tue PS GraphQL ~4h. Wed client call 1.5h. Thu Kimai sync half day. Fri docs 2h.
```

Then review rows. Only if correct:

```text
Save those LogEase rows — I confirm.
```

## Troubleshooting: missing tools after upgrade

If Cursor only shows the original 5 tools (`whoami`, `list_projects`, `parse_worklog`,
`list_worklogs`, `save_worklogs`) but not the Phase A/B tools, its **local MCP tool cache**
is stale — restart alone may not refresh it.

1. `cd mcp && npm run build`
2. In Cursor: **Developer: Reload Window** (or disable + re-enable the logease MCP server)
3. If still stuck, close Cursor and delete the cached folder for this workspace:
   `%USERPROFILE%\.cursor\projects\<workspace>\mcps\project-*-LogEase-logease`
4. Reopen Cursor and confirm **8 tools** appear for logease

## Dev

```powershell
npm run dev
```

(stdio — normally Cursor starts the process; use `dev` only for manual smoke tests.)

## Security notes

- Do **not** commit real passwords.
- Prefer your own account, not shared secrets in git.
- `save_worklogs` refuses unless `confirm` is true.
