# LogEase AI model setup and session explanation

Also see:
- Full session write-up: [SESSION_NOTES_2026-07-16.md](SESSION_NOTES_2026-07-16.md)
  (SimpleAI-aligned AI plan, Kimai tokens, temporary auth vs Azure)
- Spoken talk track for the room: [AI_SESSION_TALK_TRACK.md](AI_SESSION_TALK_TRACK.md)

## Decision

LogEase uses a **local-first** model chain. The default free model is
**Qwen2.5 7B** served locally with Ollama. It handles normal worklog parsing
without sending employee notes to an external provider.

Paid providers remain available as fallbacks for difficult requests:

```text
Local Qwen (Ollama or any OpenAI-compatible server)
  → OpenAI
  → OpenRouter
  → Gemini
  → built-in heuristic parser
```

The exact order and model names are controlled only in
`LogEase.Api/appsettings.json` under `Llm:ProviderOrder`. Restart the API
after changing them.

## Start the free local model

1. Install Ollama from https://ollama.com.
2. Download and run the recommended model:

   ```powershell
   ollama pull qwen2.5:7b
   ollama serve
   ```

3. Run LogEase API. Its default local endpoint is
   `http://localhost:11434/v1`.

For a stronger machine, change `Llm:Local:Model` to `qwen2.5:14b` after
pulling it. For a CPU-only laptop, `qwen2.5:3b` trades quality for speed.
LM Studio is also supported: set `Llm:Local:BaseUrl` and `Model` to its
OpenAI-compatible server values.

## appsettings configuration

Each provider has the same important settings:

- `Enabled` — whether the provider can be used.
- `BaseUrl` — required for local, OpenAI, and OpenRouter compatible APIs.
- `Model` — freely change this to any model exposed by that provider.
- `ApiKey` — leave blank in source control; use environment variables instead.
- `TimeoutSeconds` — maximum time before the next provider is tried.

Use environment variables for credentials:

```powershell
$env:OPENAI_API_KEY = "..."
$env:OPENROUTER_API_KEY = "..."
$env:GEMINI_API_KEY = "..."
```

Never commit API keys to `appsettings.json`. The checked-in configuration uses
empty key values intentionally.

Examples:

- Free only: `ProviderOrder: [ "Local" ]`
- Local then OpenAI: `ProviderOrder: [ "Local", "OpenAi" ]`
- Local then OpenRouter: `ProviderOrder: [ "Local", "OpenRouter" ]`

## What to say in a session/demo

“LogEase runs Qwen locally first, so routine worklog notes remain on our
machine and do not incur per-request AI charges. If the local model is
unavailable or cannot produce valid JSON, LogEase automatically tries the
configured cloud model. If every AI provider fails, it still produces a safe
editable draft using the offline parser. We can change the model, provider
order, timeout, and fallback policy in appsettings without changing code.”

## Efficiency and quality checks

For each representative prompt, record:

- `parser` returned by `parseWorklog` (`local`, `openai`, `openrouter`,
  `gemini`, or `demo-heuristic`);
- response time;
- whether returned JSON was accepted;
- row completeness (all requested dates/projects);
- correct hours, project/activity match, and description quality.

Start with 20 real-but-sanitized examples. Compare Qwen 7B against the current
cloud model, then move high-risk prompts to cloud only if their local quality
is insufficient. The GraphQL `warning` field reports why heuristic fallback
was used.
