# Phase A + B — Sticky notes & agentic sources

**Phase A:** sticky notes (web + Windows) → summarize → worklog draft → you approve → save.  
**Phase B:** same draft pipeline can also pull **GitLab events** and **IMAP mail** subjects.

Related: [MCP_FOR_LOGEASE.md](MCP_FOR_LOGEASE.md), [AI_SESSION_TALK_TRACK.md](AI_SESSION_TALK_TRACK.md)

---

## What AI type is this?

| Piece | Type |
|-------|------|
| Sticky notes store | App feature (not AI) |
| Summarize notes / GitLab / mail → rows | **Narrow AI** (still one job: draft worklogs) with an **agentic-ish** gather step |
| Approve before save | **Human-in-the-loop** |
| Cursor tools | **MCP** |

Not yet: free-form chatbot over all past months (that would be RAG / Phase D).

---

## Setup

1. SQL (or restart API — it auto-creates columns/table when DB is reachable):

```text
Scripts/SqlServer_StickyNotes_Sources.sql
```

2. Restart `LogEase.Api`
3. Web: **Sticky notes** in the sidebar (`/notes`)
4. Desktop tray: **Sticky notes…** and **Work copilot…**
5. Settings: GitLab PAT + IMAP (for Phase B checkboxes)

### Work copilot bot (chat button)
- **Web:** teal **◆** button bottom-right on every signed-in page — ask, summarize, get a table, save rows
- **Windows:** tray → **Work copilot…** — same quick prompts + structured grid + save

---

## How to use

### Phase A
1. Add daily notes on **Sticky notes**
2. Pick From/To → leave **Sticky notes** checked
3. **Summarize → worklog draft**
4. Review → **Save approved draft**

### Phase B
1. Settings → paste GitLab token (`read_api`) and/or IMAP host/user/password
2. On Sticky notes page, also check **GitLab** / **Mail**
3. Summarize → same draft → approve → save

### MCP (Cursor)
After rebuild (`cd mcp && npm run build`) and reload MCP:

- `create_sticky_note` / `list_sticky_notes`
- `draft_worklog_from_sources` (notes / gitlab / mail flags)
- `save_worklogs` with `confirm: true`

Example:

```text
Create a LogEase sticky note for today: fixed GraphQL parse bug ~3h on Perception Survey.
Then draft worklogs from sticky notes for this week and show me the rows before saving.
```

---

## One-liner for the session

> “Notes (and later GitLab/mail) feed the same LogEase AI that turns messy text into a table.  
> The agent gathers sources; you still approve the insert.”
