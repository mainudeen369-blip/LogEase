#!/usr/bin/env node
/**
 * LogEase read-only SQL MCP — direct connection string demo for sessions.
 * stdout is reserved for MCP; log to stderr only.
 */
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { createSqlClientFromEnv, DEMO_QUERIES } from "./sqlClient.js";
function text(data) {
    return { content: [{ type: "text", text: JSON.stringify(data, null, 2) }] };
}
function errText(message) {
    return { content: [{ type: "text", text: message }], isError: true };
}
const db = createSqlClientFromEnv();
const server = new McpServer({
    name: "logease-db",
    version: "1.0.0",
});
server.tool("db_info", "Show that this MCP talks SQL Server directly (read-only). Use list_tables / run_select for data.", {}, async () => text({
    mode: "read-only SQL Server",
    connection: "LOGEASE_DB_CONNECTION_STRING env (same DB as LogEase.Api)",
    safety: [
        "SELECT only — no INSERT/UPDATE/DELETE",
        "Queries must reference logease_* tables",
        "Auto TOP 100 if you omit TOP",
    ],
    contrast: "logease MCP = business API (auth, parse, save). logease-db MCP = raw SQL demo.",
}));
server.tool("list_tables", "List LogEase tables in SQL Server (logease_* prefix) with approximate row counts.", {}, async () => {
    try {
        const tables = await db.listLogEaseTables();
        return text({ count: tables.length, tables });
    }
    catch (e) {
        return errText(e instanceof Error ? e.message : String(e));
    }
});
server.tool("describe_table", "Show columns for one LogEase table (logease_Worklogs, logease_Users, etc.).", { table: z.string().min(1).describe("Table name, e.g. logease_Worklogs") }, async ({ table }) => {
    try {
        return text(await db.describeTable(table));
    }
    catch (e) {
        return errText(e instanceof Error ? e.message : String(e));
    }
});
server.tool("run_select", "Run a read-only SELECT against LogEase SQL tables. Demo: ask natural language, get SQL, run here.", {
    sql: z.string().min(8).describe("SELECT query referencing logease_* tables"),
    maxRows: z.number().int().min(1).max(200).optional().describe("Default 100"),
}, async ({ sql: query, maxRows }) => {
    try {
        return text(await db.runSelect(query, maxRows ?? 100));
    }
    catch (e) {
        return errText(e instanceof Error ? e.message : String(e));
    }
});
server.tool("demo_worklog_hours_by_project", "Preset: total hours per project for the last N days (shows JOIN + aggregation).", { days: z.number().int().min(1).max(365).optional().describe("Default 30") }, async ({ days }) => {
    try {
        const q = DEMO_QUERIES.worklogHoursByProject(days ?? 30);
        return text({ query: q.trim(), ...(await db.runSelect(q)) });
    }
    catch (e) {
        return errText(e instanceof Error ? e.message : String(e));
    }
});
server.tool("demo_recent_ai_events", "Preset: latest AI parse events from logease_AiParseEvents (AI Ops observability).", { limit: z.number().int().min(1).max(100).optional().describe("Default 20") }, async ({ limit }) => {
    try {
        const q = DEMO_QUERIES.recentAiParseEvents(limit ?? 20);
        return text({ query: q.trim(), ...(await db.runSelect(q)) });
    }
    catch (e) {
        return errText(e instanceof Error ? e.message : String(e));
    }
});
server.tool("demo_sticky_notes", "Preset: sticky notes from the last 30 days with user email.", {}, async () => {
    try {
        const q = DEMO_QUERIES.stickyNotesThisMonth();
        return text({ query: q.trim(), ...(await db.runSelect(q)) });
    }
    catch (e) {
        return errText(e instanceof Error ? e.message : String(e));
    }
});
server.tool("demo_open_incidents", "Preset: open AI Ops incidents (not Resolved).", {}, async () => {
    try {
        const q = DEMO_QUERIES.openOpsIncidents();
        return text({ query: q.trim(), ...(await db.runSelect(q)) });
    }
    catch (e) {
        return errText(e instanceof Error ? e.message : String(e));
    }
});
async function main() {
    const transport = new StdioServerTransport();
    await server.connect(transport);
    console.error("[logease-mcp-db] Ready on stdio (read-only SQL)");
}
main().catch((e) => {
    console.error("[logease-mcp-db] Fatal:", e);
    process.exit(1);
});
//# sourceMappingURL=index.js.map