# LogEase — Session notes (2026-07-16)

**Status:** Temporary / demo-ready path. **Azure AD login is planned later** (replace email/password + per-user Kimai token paste flow).

**Related docs**
- [AI session talk track](AI_SESSION_TALK_TRACK.md) — free, spoken-style explanation so the room stays awake  
- [MCP for LogEase](MCP_FOR_LOGEASE.md) — simple words + Cursor demo prompts  
- [LLM provider setup](LLM_PROVIDER_SETUP.md) — local-first AI chain, appsettings, demo talking points  
- [Implementation notes](IMPL.md) — GraphQL / DB / save destination overview  
- SimpleAI reference (recent plan): `C:\HajaWorkingFolder\SimpleAI` — AI Primer portal; the **recent plan applied to LogEase** was the same hybrid idea discussed for that workstream: **local/free model first (Qwen via Ollama) → cloud fallback (OpenAI / OpenRouter / Gemini) → heuristic**, all switchable in config (see below)

---

## 1. What AI did we implement? (SimpleAI-aligned)

We followed the **recent SimpleAI-style plan** for LogEase:

| Idea from plan | What LogEase has now |
|---|---|
| Prefer free/local model for routine work | Default: Ollama `qwen2.5:7b` at `http://localhost:11434/v1` |
| Keep cloud AI as option | OpenAI → OpenRouter → Gemini in chain |
| Configurable models | `Llm:ProviderOrder` + per-provider blocks in `appsettings.json` |
| Soft failure | Offline heuristic parser if every LLM fails |
| Explain in session | [LLM_PROVIDER_SETUP.md](LLM_PROVIDER_SETUP.md) |

**Code:** `LogEase.Api/Integrations/Llm/`, `WorklogParseService`, `docs/LLM_PROVIDER_SETUP.md`.

Hardcoded Perception Survey–style description templates were removed so AI/heuristic text comes from the user’s prompt.

---

## 2. Kimai integration (temporary auth model)

### How it works today
- Office-wide **save destination**: `LocalServer` | `Kimai` | `Both` (admin + `WorklogStorage:SaveTarget` / DB override).
- Each employee stores **their own** `KimaiApiToken` on `logease_Users` (Settings UI → GraphQL `updateKimaiSettings`).
- With Kimai/Both + token: projects/activities from Kimai; saves post timesheets; lists/reminders can read Kimai when Kimai-only.
- API validates token by calling Kimai **`GET /api/users/me`** and shows the Kimai user in UI (web Settings, Windows Settings, Log work status).

### Auth today vs later
| Today (temporary) | Later (planned) |
|---|---|
| LogEase email/password login | **Azure AD / Entra ID login** |
| Employee pastes Kimai API token into LogEase | Prefer SSO / mapped identity; Kimai link may change (service account, OAuth, or still per-user token behind Azure) |

---

## 3. Doubt: Can a Kimai admin access API tokens for all employees?

**Short answer: not automatically as a shared “all tokens” vault — but a privileged admin *can* manage other users’ API tokens if Kimai permissions allow it.**

### Default / normal model
- API tokens are **per Kimai user account**.
- Each person creates tokens under **their own** Kimai profile → API Access ([Kimai user API docs](https://www.kimai.org/documentation/user-api.html)).
- A token authenticates **as that user** (posts/reads under their identity).
- Admins do **not** get a single button that exports every employee’s live secret token string for LogEase.

### What admins *can* do (permission-dependent)
Kimai has explicit permissions ([Users / permissions](https://www.kimai.org/documentation/users.html)):

| Permission | Meaning |
|---|---|
| `api-token_own_profile` | User manages **own** API tokens |
| `api-token_other_profile` | Privileged role can change API tokens on **other** users’ profiles |

So:
- **Team User / normal employee:** only their own token.
- **Admin / Super-Admin** (if granted `api-token_other_profile`): can open another user’s profile and **create/manage** API tokens for them — useful for onboarding, **not** the same as silently harvesting every existing token.
- Tokens are secrets; newly created values are typically shown at creation time — treat them like passwords.

### What LogEase should keep doing (recommended)
- **Prefer each employee pasting their own token** into LogEase Settings (current design).
- Do **not** design LogEase Admin to store all employees’ Kimai tokens centrally unless compliance explicitly requires it.
- When Azure login arrives, map Azure user → LogEase user → (optional) Kimai identity without collecting everyone’s tokens in one admin screen.

### Practical implication for your office
| Goal | Approach |
|---|---|
| Employee logs their own time to Kimai | Each user creates token → pastes into LogEase (today) |
| Admin posts on behalf of everyone | Needs a Kimai account with rights + token for that account (and Kimai rules for acting as others — usually **not** ideal) |
| Admin only configures office destination | LogEase Admin sets Local / Kimai / Both; employees still own tokens |

---

## 4. What else was implemented this session (product / UI)

### Backend
- Real Kimai client: projects, activities, timesheets, overlap handling, `/api/users/me`
- Save destination Local / Kimai / Both
- Kimai ID codec for GraphQL UUID fields
- Reminder status aware of Kimai-only logging

### Web
- Settings: Kimai token + connected Kimai user card
- Admin: worklog save destination
- AI Entry: **Customize AI wording** (how team free-text is interpreted — not hardcoded worklog templates), Clear all, resizable prompt, Export to Excel (CSV)

### Windows tray app
- Settings (API login, Kimai token, alert) — closes after successful save
- Shows Kimai connected user
- Log work opens on app start
- Clear all, **Customize AI wording** (saved per account, used on Generate), Export to Excel
- Resizable prompt (RichTextBox + drag bar), default ~2 lines
- Grid fills width (wider description, medium project)
- Tray keepalive (reduce silent exit); publish no longer auto-kills the tray process as a silent “close”
- Pending days overall ranges
- From/To dates are binding when the work text does not name a period

### Publish
- `Scripts/Publish-Desktop.ps1` → `publish/LogEase.Desktop` + Desktop shortcut

---

## 5. How to explain this in a session / demo (talk track)

1. **Problem:** staff write free-text work notes; we need structured Kimai/local worklogs.  
2. **AI (SimpleAI plan):** local Qwen first (cheap/free), cloud only if needed, config in appsettings — see LLM_PROVIDER_SETUP.  
3. **Human-in-the-loop:** generate table → edit → save / export Excel.  
4. **Kimai:** office picks where to save; each person uses **their** Kimai token (temporary until Azure).  
5. **Admin vs tokens:** office admin configures destination; Kimai admin does **not** automatically own everyone’s tokens; privileged Kimai roles *can* manage other profiles’ tokens if permitted — we still recommend self-serve tokens.  
6. **Next:** Azure login replaces temporary LogEase credentials; Kimai linking to be redesigned then.

---

## 6. Open / next (not done in this session)

- Azure AD / Entra ID authentication  
- Possibly drop or re-scope per-user Kimai token storage after SSO  
- Stronger Excel (native `.xlsx`) if CSV/SpreadsheetML is not enough  
- Formal efficiency eval harness for local vs cloud models (called out in LLM_PROVIDER_SETUP)
