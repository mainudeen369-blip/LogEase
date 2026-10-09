export type CopilotRow = {
  workDate: string;
  projectName: string;
  activityName: string;
  hours: number;
  description: string;
  confidence: string;
  projectId?: string | null;
  activityId?: string | null;
};

export type CopilotWorklog = {
  id: string;
  workDate: string;
  projectName: string;
  hours: number;
  description: string;
};

export type CopilotReply = {
  text: string;
  rows?: CopilotRow[];
  parser?: string;
  sourcesUsed?: string;
  worklogs?: CopilotWorklog[];
};

export function defaultDateRange() {
  const to = new Date();
  const from = new Date();
  from.setDate(to.getDate() - 6);
  const fmt = (d: Date) => d.toISOString().slice(0, 10);
  return { from: fmt(from), to: fmt(to) };
}

type Intent =
  | "history"
  | "sources-all"
  | "sources-notes"
  | "sources-gitlab"
  | "sources-mail"
  | "parse";

function detectIntent(message: string): Intent {
  const m = message.toLowerCase();
  if (/worklog|saved|already logged|what did i do|past work|history|logged/.test(m)) return "history";
  if (/gitlab|git lab|commits|merge request|mr /.test(m)) return "sources-gitlab";
  if (/mail|email|inbox|imap|outlook/.test(m)) return "sources-mail";
  if (/notes only|sticky notes only|just notes/.test(m)) return "sources-notes";
  if (/summarize|summary|week|month|sticky|notes|all sources|everything|draft/.test(m))
    return "sources-all";
  return "parse";
}

type GqlFn = <T>(query: string, variables?: unknown, token?: string) => Promise<T>;

export async function askWorkCopilot(
  message: string,
  token: string,
  gql: GqlFn,
): Promise<CopilotReply> {
  const trimmed = message.trim();
  if (!trimmed) {
    return { text: "Ask me something — e.g. “Summarize this week from my notes” or paste messy work text." };
  }

  const { from, to } = defaultDateRange();
  const intent = detectIntent(trimmed);

  if (intent === "history") {
    const data = await gql<{ worklogs: CopilotWorklog[] }>(
      `query ($from: String, $to: String) {
        worklogs(from: $from, to: $to) {
          id workDate projectName hours description
        }
      }`,
      { from, to },
      token,
    );
    const logs = data.worklogs ?? [];
    if (logs.length === 0) {
      return {
        text: `No saved worklogs between ${from} and ${to}. Try “Summarize this week from my notes”.`,
        worklogs: [],
      };
    }
    const lines = logs.map(
      (w) => `${w.workDate} · ${w.projectName} · ${w.hours}h — ${w.description}`,
    );
    return {
      text: `Found ${logs.length} saved row(s) for ${from} → ${to}:\n\n${lines.join("\n")}`,
      worklogs: logs,
    };
  }

  const includeNotes = intent === "sources-all" || intent === "sources-notes";
  const includeGitlab = intent === "sources-all" || intent === "sources-gitlab";
  const includeMail = intent === "sources-all" || intent === "sources-mail";

  if (intent !== "parse") {
    const data = await gql<{
      draftWorklogFromSources: {
        rows: CopilotRow[];
        parser: string;
        warning?: string | null;
        sourcesUsed: string;
        combinedPreview?: string | null;
      };
    }>(
      `mutation ($input: DraftFromSourcesInput!) {
        draftWorklogFromSources(input: $input) {
          parser warning sourcesUsed combinedPreview
          rows {
            workDate projectName activityName hours description confidence projectId activityId
          }
        }
      }`,
      {
        input: {
          dateFrom: from,
          dateTo: to,
          includeNotes,
          includeGitlab,
          includeMail,
        },
      },
      token,
    );
    const p = data.draftWorklogFromSources;
    const intro =
      intent === "sources-notes"
        ? "Summarized from your sticky notes:"
        : intent === "sources-gitlab"
          ? "Summarized from GitLab activity (+ notes if checked in Settings flow):"
          : intent === "sources-mail"
            ? "Summarized from mail subjects (+ notes):"
            : "Summarized from your selected sources:";
    return {
      text: `${intro}\nParser: ${p.parser}. Sources: ${p.sourcesUsed || "—"}.${
        p.warning ? `\nNote: ${p.warning}` : ""
      }\n\nReview the structured table below — edit on AI Entry or save from here.`,
      rows: p.rows,
      parser: p.parser,
      sourcesUsed: p.sourcesUsed,
    };
  }

  const data = await gql<{
    parseWorklog: { rows: CopilotRow[]; parser: string; warning?: string | null };
  }>(
    `mutation ($input: ParseWorklogInput!) {
      parseWorklog(input: $input) {
        parser warning
        rows {
          workDate projectName activityName hours description confidence projectId activityId
        }
      }
    }`,
    { input: { text: trimmed, dateFrom: from, dateTo: to } },
    token,
  );
  const p = data.parseWorklog;
  return {
    text: `Parsed your text into worklog rows (parser: ${p.parser}).${
      p.warning ? ` ${p.warning}` : ""
    }`,
    rows: p.rows,
    parser: p.parser,
  };
}

export const QUICK_PROMPTS = [
  "Summarize this week from my sticky notes",
  "What work did I already log this week?",
  "Summarize from GitLab and notes",
  "Summarize from mail and notes",
];
