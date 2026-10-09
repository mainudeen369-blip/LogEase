# MCP for LogEase — simple words

Also published as a visual session page in SimpleAI:  
`http://localhost:3000/sessionimplementation` (repo: `C:\HajaWorkingFolder\SimpleAI`).

**Related**
- Talk track: [AI_SESSION_TALK_TRACK.md](AI_SESSION_TALK_TRACK.md)
- LLM setup: [LLM_PROVIDER_SETUP.md](LLM_PROVIDER_SETUP.md)
- How to run the server: [`../mcp/README.md`](../mcp/README.md)

---

## What is MCP? (30-second version)

**MCP = Model Context Protocol.**

Think of it as a **USB standard for AI tools** — or a **hub in the middle**:

| Side | What |
|------|------|
| **Left** | AI apps (Cursor chat, Claude Desktop, other IDEs) |
| **Center** | MCP — the agreed plug / protocol |
| **Right** | Data & tools (DB, Git, Slack…) — for us, **LogEase** |

- Your LLM (in Cursor, Claude, etc.) is the brain.
- MCP is the plug shape everyone agrees on.
- LogEase MCP server is a gadget: “parse worklogs”, “list projects”, “save rows”.

Without MCP, Cursor only knows what you paste into chat.  
With MCP, Cursor can **call LogEase** the same way the web UI and desktop app do.

Session page (with the hub diagram): `http://localhost:3000/sessionimplementation`  
Topic images: SimpleAI → Learn → MCP → **Image to understand**

---

## What we built

```text
You type in Cursor
        │
        ▼
  Cursor AI (brain)
        │  “I need the parse_worklog tool”
        ▼
  LogEase MCP server   ← small Node process
        │  GraphQL + your login token
        ▼
  LogEase.Api          ← same engine as AI Entry
        │
        └── local Qwen / cloud / heuristic → rows
```

**Important:** MCP does **not** replace the AI parser.  
It opens a **new door** into the same LogEase factory.

| Door | Who uses it |
|------|-------------|
| Web AI Entry | Browser |
| Desktop tray | WinForms |
| **MCP** | Cursor (and other MCP clients) |

---

## Tools you can call from Cursor

| Tool | Plain meaning |
|------|----------------|
| `whoami` | Who am I logged in as in LogEase? |
| `list_projects` | Show projects & activities |
| `parse_worklog` | Messy notes → draft worklog table (uses LogEase AI) |
| `list_worklogs` | Show saved rows for a date range |
| `list_sticky_notes` / `create_sticky_note` | Phase A daily notes |
| `draft_worklog_from_sources` | Notes ± GitLab ± mail → draft (Phase A/B) |
| `save_worklogs` | Save rows (needs `confirm: true` — no silent saves) |

---

## How to talk in a session

> “We already taught LogEase to turn free text into a table.  
> MCP lets Cursor plug into that same feature.  
> I stay in the IDE, ask in chat, and LogEase does the work —  
> including our local-first AI chain.”

One line:

> **LLM = brain. MCP = USB. LogEase tools = the gadgets.**

---

## Demo prompts (copy/paste in Cursor)

After MCP is connected and `LogEase.Api` is running:

1. `Who am I in LogEase?` → should call `whoami`
2. `List my LogEase projects` → `list_projects`
3. Then:

```text
Using LogEase MCP, parse this worklog for 2026-07-14 to 2026-07-18:

Mon-Tue: Perception Survey GraphQL fixes ~4h.
Wed client call ~1.5h.
Thu Kimai sync debugging half day.
Fri docs + standup ~2h.
```

4. Review the rows. Only if they look right:

```text
Save those LogEase rows — I confirm.
```

(The tool requires `confirm: true` so we keep human-in-the-loop.)

---

## What MCP is *not*

- Not a new ChatGPT inside LogEase  
- Not RAG / vector search  
- Not “agents that roam free” by themselves  
- Not a replacement for the web/desktop UI  

It’s a **standard plug** so AI clients can use your app’s actions safely.

---

## Setup (short)

1. Start API: `cd LogEase.Api && dotnet run` → `http://localhost:5055/graphql`
2. Install MCP: `cd mcp && npm install && npm run build`
3. Add Cursor MCP config (see [`mcp/README.md`](../mcp/README.md) and `mcp/cursor-mcp.example.json`)
4. Restart Cursor / reload MCP
5. Chat with tools enabled

Demo account (from main README): `demo@logease.local` / `Demo@123`

---

*Same LogEase brain. New door. That’s MCP.*
