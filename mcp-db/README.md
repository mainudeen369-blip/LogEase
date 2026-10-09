# LogEase MCP — direct SQL (read-only demo)

**For sessions:** shows that MCP can connect to SQL Server with a connection string — not only GraphQL.

| MCP server | How it talks to data | Best for |
|------------|----------------------|----------|
| `logease` | GraphQL API (login, business rules) | Parse worklogs, save rows, sticky notes |
| `logease-db` | **Connection string → SQL Server** | Ad-hoc SELECT, schema explore, analytics demos |

## Safety (demo)

- **SELECT only** — writes blocked
- Must reference `logease_*` tables
- Auto `TOP 100` if omitted
- Connection string only in env — never commit passwords

## Install

```powershell
cd C:\HajaWorkingFolder\LogEase\mcp-db
npm install
npm run build
```

## Cursor config

Add next to your existing `logease` block in `.cursor/mcp.json`:

```json
"logease-db": {
  "command": "node",
  "args": ["C:/HajaWorkingFolder/LogEase/mcp-db/dist/index.js"],
  "env": {
    "LOGEASE_DB_CONNECTION_STRING": "Server=...;Database=...;User Id=...;Password=...;Encrypt=True;TrustServerCertificate=True"
  }
}
```

Copy the connection string from `LogEase.Api/appsettings.json` → `ConnectionStrings:DefaultConnection`.

Reload MCP in Cursor. You should see **7 tools** on `logease-db`.

## Demo prompts (Cursor chat)

```text
Using logease-db MCP, list all LogEase tables and row counts.
```

```text
Using logease-db, show columns on logease_Worklogs, then run:
SELECT TOP 10 WorkDate, Hours, Description FROM dbo.logease_Worklogs ORDER BY WorkDate DESC
```

```text
Using logease-db demo tools, show hours by project last 30 days and recent AI parse events.
```

## Why two servers?

Production apps usually keep **one door** (API) so auth and rules stay in C#.  
For teaching MCP, a **second door** (read-only SQL) makes the protocol click: *any capability can be a tool*.
