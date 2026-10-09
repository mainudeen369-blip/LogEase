#!/usr/bin/env node
/**
 * LogEase MCP server — stdio transport for Cursor.
 * Do not console.log to stdout; use stderr only.
 */
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { createClientFromEnv, type SaveItem } from "./logeaseClient.js";

function text(data: unknown) {
  return {
    content: [{ type: "text" as const, text: JSON.stringify(data, null, 2) }],
  };
}

function errText(message: string) {
  return {
    content: [{ type: "text" as const, text: message }],
    isError: true as const,
  };
}

const client = createClientFromEnv();

const server = new McpServer({
  name: "logease",
  version: "1.1.0",
});

server.tool(
  "whoami",
  "Show the current LogEase user and office save destination (Local / Kimai / Both).",
  {},
  async () => {
    try {
      const data = await client.whoami();
      return text({
        user: data.me,
        saveDestination: data.appSettings,
        tip: "This MCP server uses the same LogEase.Api as the web and desktop apps.",
      });
    } catch (e) {
      return errText(e instanceof Error ? e.message : String(e));
    }
  },
);

server.tool(
  "list_projects",
  "List LogEase (or Kimai) projects and activities available for worklogs.",
  {},
  async () => {
    try {
      const projects = await client.listProjects();
      return text({
        count: projects.length,
        projects,
        tip: "Use projectId and activityId from here when calling save_worklogs.",
      });
    } catch (e) {
      return errText(e instanceof Error ? e.message : String(e));
    }
  },
);

server.tool(
  "parse_worklog",
  "Turn free-text work notes into structured worklog draft rows using LogEase AI " +
    "(local Qwen first, then cloud fallback, then heuristic). " +
    "Always show the rows to the user before save_worklogs.",
  {
    text: z
      .string()
      .min(1)
      .describe("Messy free-text notes describing work (projects, hours, days)."),
    dateFrom: z
      .string()
      .regex(/^\d{4}-\d{2}-\d{2}$/)
      .describe("Period start YYYY-MM-DD (UI From date)."),
    dateTo: z
      .string()
      .regex(/^\d{4}-\d{2}-\d{2}$/)
      .describe("Period end YYYY-MM-DD (UI To date)."),
  },
  async ({ text: note, dateFrom, dateTo }) => {
    try {
      const result = await client.parseWorklog(note, dateFrom, dateTo);
      return text({
        ...result,
        nextStep:
          "Review rows with the user. If they approve, call save_worklogs with confirm=true " +
          "and the same projectId/activityId values.",
      });
    } catch (e) {
      return errText(e instanceof Error ? e.message : String(e));
    }
  },
);

server.tool(
  "list_worklogs",
  "List worklogs already saved in LogEase for a date range.",
  {
    dateFrom: z
      .string()
      .regex(/^\d{4}-\d{2}-\d{2}$/)
      .describe("From date YYYY-MM-DD"),
    dateTo: z
      .string()
      .regex(/^\d{4}-\d{2}-\d{2}$/)
      .describe("To date YYYY-MM-DD"),
  },
  async ({ dateFrom, dateTo }) => {
    try {
      const worklogs = await client.listWorklogs(dateFrom, dateTo);
      return text({ count: worklogs.length, worklogs });
    } catch (e) {
      return errText(e instanceof Error ? e.message : String(e));
    }
  },
);

server.tool(
  "list_sticky_notes",
  "List sticky notes (Phase A daily notes) for an optional date range.",
  {
    dateFrom: z.string().regex(/^\d{4}-\d{2}-\d{2}$/).optional(),
    dateTo: z.string().regex(/^\d{4}-\d{2}-\d{2}$/).optional(),
  },
  async ({ dateFrom, dateTo }) => {
    try {
      const notes = await client.listStickyNotes(dateFrom, dateTo);
      return text({ count: notes.length, notes });
    } catch (e) {
      return errText(e instanceof Error ? e.message : String(e));
    }
  },
);

server.tool(
  "create_sticky_note",
  "Add a daily sticky note in LogEase (Phase A).",
  {
    noteDate: z.string().regex(/^\d{4}-\d{2}-\d{2}$/),
    body: z.string().min(1).max(4000),
  },
  async ({ noteDate, body }) => {
    try {
      const created = await client.createStickyNote(noteDate, body);
      return text({ ok: true, ...created });
    } catch (e) {
      return errText(e instanceof Error ? e.message : String(e));
    }
  },
);

server.tool(
  "draft_worklog_from_sources",
  "Phase A/B agentic draft: summarize sticky notes and/or GitLab events and/or IMAP mail " +
    "into worklog rows. Always show the draft to the user before save_worklogs.",
  {
    dateFrom: z.string().regex(/^\d{4}-\d{2}-\d{2}$/),
    dateTo: z.string().regex(/^\d{4}-\d{2}-\d{2}$/),
    includeNotes: z.boolean().optional().describe("Default true"),
    includeGitlab: z.boolean().optional().describe("Needs GitLab token in LogEase Settings"),
    includeMail: z.boolean().optional().describe("Needs IMAP settings in LogEase Settings"),
  },
  async (args) => {
    try {
      const result = await client.draftFromSources(args);
      return text({
        ...result,
        nextStep:
          "Review rows with the user. If they approve, call save_worklogs with confirm=true.",
      });
    } catch (e) {
      return errText(e instanceof Error ? e.message : String(e));
    }
  },
);

server.tool(
  "save_worklogs",
  "Save approved worklog rows to LogEase (and/or Kimai per office settings). " +
    "REQUIRES confirm=true. Never call this without the user explicitly approving the rows.",
  {
    confirm: z
      .boolean()
      .describe("Must be true. Safety lock — refuse to save unless user approved."),
    items: z
      .array(
        z.object({
          workDate: z.string().regex(/^\d{4}-\d{2}-\d{2}$/),
          projectId: z.string().uuid().describe("GUID from parse_worklog or list_projects"),
          activityId: z.string().uuid().describe("GUID from parse_worklog or list_projects"),
          hours: z.number().min(0.25).max(24),
          description: z.string().min(1),
        }),
      )
      .min(1)
      .describe("Rows to save (must include real projectId and activityId)."),
  },
  async ({ confirm, items }) => {
    if (!confirm) {
      return errText(
        "Refused: confirm must be true. Show the rows to the user and ask them to approve first.",
      );
    }
    try {
      const payload: SaveItem[] = items.map((i) => ({
        workDate: i.workDate,
        projectId: i.projectId,
        activityId: i.activityId,
        hours: i.hours,
        description: i.description,
      }));
      const result = await client.saveWorklogs(payload);
      return text({
        ok: true,
        ...result,
        tip: "Rows saved through the same saveWorklogs GraphQL mutation as the UI.",
      });
    } catch (e) {
      return errText(e instanceof Error ? e.message : String(e));
    }
  },
);

async function main() {
  const transport = new StdioServerTransport();
  await server.connect(transport);
  console.error("[logease-mcp] Ready on stdio — waiting for Cursor.");
}

main().catch((err) => {
  console.error("[logease-mcp] Fatal:", err);
  process.exit(1);
});
