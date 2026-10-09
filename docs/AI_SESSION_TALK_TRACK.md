# LogEase AI session — talk track (keep it human)

Use this when you present. Speak it like a story, not a slide dump.  
**Goal:** people leave understanding *what we built* and *why AI here is useful* — without zoning out.

**Related**
- Setup / config: [LLM_PROVIDER_SETUP.md](LLM_PROVIDER_SETUP.md)
- Build notes: [SESSION_NOTES_2026-07-16.md](SESSION_NOTES_2026-07-16.md)
- MCP (Cursor plug): [MCP_FOR_LOGEASE.md](MCP_FOR_LOGEASE.md)
- Concepts portal: SimpleAI (`C:\HajaWorkingFolder\SimpleAI`)

**Suggested length:** ~20–30 minutes + 5 min live demo.

---

## Opening hook (30 seconds)

> “Every Friday someone types: *‘did PS stuff, fixed Kimai bug, call with client — maybe 3 hours?’*  
> Finance and Kimai don’t want a poem. They want **rows**: project, activity, hours, date.  
> LogEase is the translator. AI reads the poem. You approve the rows. Done.”

Pause. Let that land. Then:

> “We didn’t build a chatbot that chats forever. We built a **small, useful AI** — one job, done well.”

---

## The one idea to remember

Write this on a board / slide if you want:

**Messy human notes → AI draft table → you edit → save.**

That’s the whole product story. Everything else is “how.”

---

## Part 1 — What is this AI, in plain words? (~5 min)

### Don’t say
“We integrated a multi-provider LLM orchestration pipeline with structured output…”

### Do say

> “Think of AI here like a smart intern who is great at **reading messy notes** and filling a spreadsheet.  
> Fast. Sometimes wrong. Never the final boss — **you** are.”

**Narrow AI (SimpleAI word, simple meaning):**  
> “Not ‘artificial general intelligence that runs the company.’  
> One narrow job: turn free text into worklog rows. Like a calculator, but for language.”

**Prompt (the instruction letter):**  
> “We don’t ‘train a new brain’ every time someone logs work.  
> We send a clear letter: *Here’s the text. Here’s the date range. Reply only as this JSON table.*  
> That letter is the prompt. Better letter → better answer.”

**System prompt vs your notes:**  
> “System prompt = office rules (‘always return JSON, respect From/To dates…’).  
> Your paste box = today’s diary.  
> Customize AI wording = teach the intern our slang — ‘PS’ means Perception Survey, ‘Haja’ means that project nickname.”

---

## Part 2 — Why local first? (the privacy / money story) (~4 min)

> “You can send notes to OpenAI or Gemini in the cloud. That works. It also means:  
> *someone else’s computer reads your work diary* — and you often **pay per request**.”

> “So our default is: run a model **on our machine** with Ollama — we use **Qwen**.  
> Notes stay local for the routine stuff. No drama. No invoice for every ‘2h bugfix.’”

**The chain (tell it like a backup plan, not architecture):**

```text
Try local Qwen (free-ish, private)
  → if that fails, try cloud
  → if cloud fails too, use our offline “best guess” parser
  → you still get an editable table either way
```

> “Same idea as: try Wi‑Fi, then mobile data, then write it by hand. We never leave you with a blank screen.”

**One line for managers:**  
> “Config in `appsettings` — change model or order without rewriting the app.”

---

## Part 3 — Live demo (the part they remember) (~8 min)

Do this with a real-looking messy prompt. Speak while it runs.

### Demo script

1. Open **AI Entry** (web) or tray **Log work…**
2. Paste something ugly on purpose:

   > “Mon-Tue: PS GraphQL fixes ~4h. Wed client call Perception ~1.5. Thu Kimai sync debugging half day. Fri docs + standup.”

3. Hit **Generate**. While waiting:

   > “Behind the curtain: API builds the prompt, asks the model for JSON rows, cleans them up, shows the grid.”

4. Point at the table:

   > “AI gave us a draft. Now the important bit — **human-in-the-loop**. Fix a project name. Fix hours. We’re not auto-posting blind.”

5. Optional: open **Customize AI wording**

   > “If the team always writes ‘PS’ or typos a project, teach it once here — no hardcoded templates.”

6. Save / export Excel briefly.

   > “Draft → approve → Kimai or local DB. AI helped; you signed off.”

### If local model is slow

> “Local models on laptop CPU can think for a few seconds. That’s the tradeoff for privacy. Cloud is faster when we need it.”

### If it falls back to heuristic

> “See that warning? Every AI provider was busy or failed — so we used the offline parser. Still editable. Still useful. Soft landing.”

---

## Part 4 — Map to SimpleAI topics (only what we actually use) (~5 min)

Don’t walk through 56 glossary terms. Pick **these**, with LogEase as the proof:

| Fancy word (SimpleAI) | Say it like this | Where in LogEase |
|---|---|---|
| **LLM** | A model that predicts the next useful words / structure | Qwen / cloud models |
| **Ollama** | App that runs models on your PC | Local provider |
| **Prompt engineering** | Writing clear instructions so the answer is usable | System + user prompts |
| **Structured output** | “Don’t chat — fill the form (JSON)” | Parse → table rows |
| **Model routing** | Try A, then B, then C | Provider chain |
| **Privacy & local AI** | Keep routine notes off the public cloud | Local-first default |
| **Human-in-the-loop** | AI drafts, human decides | Edit before save |
| **Narrow AI** | One job, not a general assistant | Worklog parsing only |
| **Observability** | See which provider ran, how long, fallbacks | AI Ops → Observability |
| **Incident response** | Detect → triage → mitigate → resolve | AI Ops → Incidents |
| **Technical debt** | Shortcuts we track so we pay them down | AI Ops → Debt list |

**Topics we teach in SimpleAI but did *not* fully build in LogEase yet** (say once, honestly):

> “RAG, long-running agents — those are still learning-portal topics.  
> LogEase has the practical pattern: **prompt → structured draft**, plus **MCP** so Cursor can call the same tools.  
> **New for this session:** Observability, Incident response, and Technical debt are live under **AI Ops** — and in SimpleAI’s left panel under Practical Usage.”

---

## Part 4b — AI Ops demo (observability / incidents / debt) (~4 min)

Open **AI Ops** in LogEase (nav). Speak while pointing:

1. **Observability** — “Every Generate writes a row: which parser, how many ms, did we fall back?”  
   Run AI Entry once → Refresh AI Ops → show the new event.
2. **Incident response** — “If AI dies and we use heuristic, we auto-open an incident. Status moves Open → Investigating → Mitigated → Resolved.”
3. **Technical debt** — “We list real shortcuts: no golden evals, unversioned Customize-AI wording. Debt is visible, not a forgotten wiki.”

Then open SimpleAI → left panel → **Observability / Incident Response / Technical Debt** for the vocabulary.

---

## Part 5 — What is MCP? (show it live if wired)

> “MCP is like a **USB standard for AI tools** — or a hub in the middle.  
> Left: Cursor / chat apps. Center: MCP. Right: LogEase, Git, databases…  
> LogEase MCP server exposes parse / projects / save.  
> Cursor plugs in — I stay in chat, LogEase does the work.”

Show the hub diagram on SimpleAI → **Session Implementation** (`/sessionimplementation`) or Learn → MCP → Image to understand.

Live (optional): ask Cursor to `parse_worklog` with a messy note.  
Details: [MCP_FOR_LOGEASE.md](MCP_FOR_LOGEASE.md).

Then stop — don’t turn MCP into a second full talk.

---

## Part 6 — Closing (what you want them to walk away with)

Three bullets. Say them slowly:

1. **AI in LogEase = translator**, not a boss.  
2. **Local first** for privacy and cost; cloud as backup.  
3. **You still review** — that’s how you trust AI at work.

Optional closer:

> “If you want the vocabulary — RAG, agents, MCP, embeddings — open SimpleAI.  
> If you want to *feel* AI in a real office tool — open LogEase and paste your week.”

---

## Energy tips (so they don’t get bored)

- **Max 1 jargon word at a time**, then an analogy.
- Demo early — eyes glaze after 10 minutes of theory.
- Ask one question mid-way: *“Who still copies notes into Kimai by hand?”* Hands go up → you’re relevant.
- Never read code on screen unless someone asks “how?”
- If Q&A goes deep (Azure, Kimai tokens, architecture) → park it: “That’s in the session notes; happy to walk through after.”

---

## Cheat sheet — one-liners if you freeze

| They ask… | You say… |
|---|---|
| Is this ChatGPT? | “Same family of models, different job — we force a worklog table, not a chat.” |
| Is my data safe? | “Default path is local Ollama. Cloud only if we configure it and local can’t finish.” |
| What if AI is wrong? | “You edit the grid. Wrong rows don’t auto-save.” |
| Why not always cloud? | “Cost + privacy. Local handles most boring, repetitive notes.” |
| Did you use LangChain / agents? | “No. Plain HTTP to the model. MCP is a separate plug for Cursor tools.” |
| What’s next? | “Azure login; MCP is live — parse from Cursor. Core AI loop already works.” |
| What’s MCP? | “USB for AI tools — Cursor calls LogEase parse/save without opening the UI.” |

---

## Sample 25-minute agenda

| Min | What |
|-----|------|
| 0–2 | Hook: poem → rows |
| 2–7 | Plain words: intern, prompt, narrow job |
| 7–11 | Local first + backup chain |
| 11–19 | Live demo + customize wording |
| 19–23 | SimpleAI map (table talk) + optional MCP one-liner |
| 23–25 | Three takeaways + Q&A |

---

*Speak the story. Show the grid. Let AI be the helper, not the hero.*
