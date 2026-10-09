import sql from "mssql";

const BLOCKED =
  /\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|EXEC|EXECUTE|CREATE|MERGE|GRANT|REVOKE|INTO)\b/i;

const LOGEASE_TABLE = /^logease_[A-Za-z0-9_]+$/;

function log(...args: unknown[]) {
  console.error("[logease-mcp-db]", ...args);
}

export class SqlClient {
  private pool: sql.ConnectionPool | null = null;

  constructor(private readonly connectionString: string) {}

  async connect(): Promise<void> {
    if (this.pool?.connected) return;
    this.pool = await new sql.ConnectionPool(this.connectionString).connect();
    log("Connected to SQL Server");
  }

  async close(): Promise<void> {
    if (this.pool) {
      await this.pool.close();
      this.pool = null;
    }
  }

  async listLogEaseTables(): Promise<{ schema: string; name: string; rows: number | null }[]> {
    await this.connect();
    const result = await this.pool!.request().query(`
      SELECT
        s.name AS [schema],
        t.name AS [name],
        SUM(p.rows) AS [rows]
      FROM sys.tables t
      INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
      LEFT JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
      WHERE t.name LIKE 'logease[_]%'
      GROUP BY s.name, t.name
      ORDER BY t.name
    `);
    return result.recordset.map((r) => ({
      schema: r.schema,
      name: r.name,
      rows: r.rows ?? null,
    }));
  }

  async describeTable(tableName: string): Promise<{
    table: string;
    columns: { name: string; type: string; nullable: boolean; maxLength: number | null }[];
  }> {
    const safe = assertLogEaseTable(tableName);
    await this.connect();
    const result = await this.pool!.request().input("table", sql.NVarChar, safe).query(`
      SELECT
        c.name AS columnName,
        ty.name AS dataType,
        c.max_length AS maxLength,
        c.is_nullable AS isNullable
      FROM sys.columns c
      INNER JOIN sys.types ty ON c.user_type_id = ty.user_type_id
      INNER JOIN sys.tables t ON c.object_id = t.object_id
      WHERE t.name = @table
      ORDER BY c.column_id
    `);
    return {
      table: safe,
      columns: result.recordset.map((c) => ({
        name: c.columnName,
        type: c.dataType,
        nullable: Boolean(c.isNullable),
        maxLength: c.maxLength ?? null,
      })),
    };
  }

  async runSelect(query: string, maxRows = 100): Promise<{
    rowCount: number;
    columns: string[];
    rows: Record<string, unknown>[];
    note: string;
  }> {
    const sqlText = validateSelectOnly(query, maxRows);
    await this.connect();
    const result = await this.pool!.request().query(sqlText);
    const rows = result.recordset as Record<string, unknown>[];
    const columns = rows.length > 0 ? Object.keys(rows[0]) : [];
    return {
      rowCount: rows.length,
      columns,
      rows,
      note: `Read-only SELECT (max ${maxRows} rows).`,
    };
  }
}

export function createSqlClientFromEnv(): SqlClient {
  const cs = (process.env.LOGEASE_DB_CONNECTION_STRING ?? "").trim();
  if (!cs) {
    throw new Error(
      "Set LOGEASE_DB_CONNECTION_STRING in MCP env (same as LogEase.Api DefaultConnection). " +
        "See mcp-db/README.md",
    );
  }
  return new SqlClient(cs);
}

function assertLogEaseTable(name: string): string {
  const base = name.includes(".") ? name.split(".").pop()! : name;
  const clean = base.replace(/[\[\]]/g, "").trim();
  if (!LOGEASE_TABLE.test(clean)) {
    throw new Error(`Only logease_* tables are allowed for describe_table (got: ${name})`);
  }
  return clean;
}

function validateSelectOnly(query: string, maxRows: number): string {
  let q = query.trim().replace(/;+\s*$/g, "");
  if (!q) throw new Error("SQL query is empty");
  if (q.includes(";")) throw new Error("Only one statement allowed (no semicolons)");
  if (!/^select\b/i.test(q)) throw new Error("Only SELECT queries are allowed");
  if (BLOCKED.test(q)) throw new Error("Query contains blocked keywords (write/DDL/exec)");

  // Demo safety: queries should touch LogEase tables only
  if (!/logease_/i.test(q)) {
    throw new Error("Query must reference at least one logease_* table");
  }

  if (!/\btop\s+\d+/i.test(q)) {
    q = q.replace(/^select\b/i, `SELECT TOP ${maxRows}`);
  }
  return q;
}

export const DEMO_QUERIES = {
  worklogHoursByProject: (days: number) => `
    SELECT TOP 50
      p.Name AS Project,
      COUNT(*) AS EntryCount,
      SUM(w.Hours) AS TotalHours
    FROM dbo.logease_Worklogs w
    INNER JOIN dbo.logease_Projects p ON w.ProjectId = p.Id
    WHERE w.WorkDate >= DATEADD(day, -${Math.min(Math.max(days, 1), 365)}, CAST(GETDATE() AS date))
    GROUP BY p.Name
    ORDER BY TotalHours DESC
  `,
  recentAiParseEvents: (limit: number) => `
    SELECT TOP ${Math.min(Math.max(limit, 1), 100)}
      CreatedAt, Parser, DurationMs, RowCount, UsedFallback, Success, Source
    FROM dbo.logease_AiParseEvents
    ORDER BY CreatedAt DESC
  `,
  stickyNotesThisMonth: () => `
    SELECT TOP 50
      u.Email,
      n.NoteDate,
      LEFT(n.Body, 120) AS NotePreview
    FROM dbo.logease_StickyNotes n
    INNER JOIN dbo.logease_Users u ON n.UserId = u.Id
    WHERE n.NoteDate >= DATEADD(day, -30, CAST(GETDATE() AS date))
    ORDER BY n.NoteDate DESC
  `,
  openOpsIncidents: () => `
    SELECT TOP 20
      CreatedAt, Title, Status, Severity, Category
    FROM dbo.logease_OpsIncidents
    WHERE Status <> 'Resolved'
    ORDER BY CreatedAt DESC
  `,
};
