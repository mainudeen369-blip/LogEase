# LogEase — Implementation notes

## Goal

Prompt (+ date range) → structured rows → human edit → SQL and/or Kimai → filtered worklogs. Reminder at office end (web + Windows tray).

**Session write-up (2026-07-16):** [SESSION_NOTES_2026-07-16.md](SESSION_NOTES_2026-07-16.md) — AI chain (SimpleAI-aligned), Kimai tokens, UI, and “is admin able to use all employee tokens?”.

## Database

- Run **`Scripts/SqlServer_All.sql`** on your test DB first.
- Tables: **`logease_Users`** (includes `KimaiApiToken`), **`logease_Projects`**, **`logease_Activities`**, **`logease_Worklogs`**, **`logease_AppSettings`** (dbo, name prefix).
- `Database:UseEnsureCreated` = false by default.

## GraphQL

- `login`, `projects`, `parseWorklog`, `saveWorklogs`, `worklogs`, `reminderStatus`, `updateReminder`, `updateKimaiSettings`, `me` (includes `kimaiUser` when token works), `appSettings`, `updateAppSettings`

## Worklog save destination

- `WorklogStorage:SaveTarget` in appsettings: `LocalServer` | `Kimai` | `Both`
- Admin page can override (stored in `logease_AppSettings`)
- Employee Kimai token still required when target includes Kimai (**temporary** until Azure login)
- `Kimai:BaseUrl` required for Kimai / Both

## Parser

1. Configured local-first LLM provider chain  
2. Heuristic demo parser if every provider is unavailable or fails

## AI provider chain

The parser now uses the configurable local-first chain in `Llm:ProviderOrder`:
local Qwen (Ollama/LM Studio) → OpenAI → OpenRouter → Gemini → heuristic.
Aligned with the recent SimpleAI hybrid plan (local/free first, cloud fallback, config-driven).
See [LLM provider setup](LLM_PROVIDER_SETUP.md) for model configuration and
efficiency evaluation guidance.
