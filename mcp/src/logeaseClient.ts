/**
 * Thin GraphQL client for LogEase.Api — same operations as LogEase.Desktop.
 * Logs go to stderr only (stdout is reserved for MCP stdio).
 */

export type ParsedRow = {
  workDate: string;
  projectName: string;
  activityName: string;
  hours: number;
  description: string;
  confidence: string;
  projectId: string | null;
  activityId: string | null;
};

export type ProjectInfo = {
  id: string;
  name: string;
  activities: { id: string; name: string; projectId: string }[];
};

export type WorklogInfo = {
  id: string;
  workDate: string;
  projectName: string;
  hours: number;
  description: string;
};

export type SaveItem = {
  workDate: string;
  projectId: string;
  activityId: string;
  hours: number;
  description: string;
};

function log(...args: unknown[]) {
  console.error("[logease-mcp]", ...args);
}

export class LogEaseClient {
  private token: string | null = null;

  constructor(
    private readonly apiUrl: string,
    private readonly email: string,
    private readonly password: string,
  ) {}

  clearAuth(): void {
    this.token = null;
  }

  async ensureAuth(force = false): Promise<void> {
    if (this.token && !force) return;
    this.token = null;
    const data = await this.post<{
      login: { success: boolean; token?: string | null; error?: string | null; name?: string | null; email?: string | null };
    }>(
      null,
      `mutation ($input: LoginInput!) {
        login(input: $input) {
          success token error name email
        }
      }`,
      { input: { email: this.email, password: this.password } },
    );

    if (!data.login?.success || !data.login.token) {
      throw new Error(
        (data.login?.error ?? "LogEase login failed") +
          ` (user=${this.email}, api=${this.apiUrl})`,
      );
    }
    this.token = data.login.token;
    log(`Logged in as ${data.login.email ?? this.email}`);
  }

  async whoami() {
    return this.withAuth(() =>
      this.post<{
        me: {
          email: string;
          name: string;
          hasKimaiToken: boolean;
          kimaiError?: string | null;
          aiParseInstructions?: string | null;
        } | null;
        appSettings: {
          worklogSaveTarget: string;
          worklogSaveTargetLabel: string;
          kimaiBaseUrlConfigured: boolean;
        };
      }>(
        this.token,
        `query {
        me {
          email name hasKimaiToken kimaiError aiParseInstructions
        }
        appSettings {
          worklogSaveTarget worklogSaveTargetLabel kimaiBaseUrlConfigured
        }
      }`,
        {},
      ),
    );
  }

  async listProjects(): Promise<ProjectInfo[]> {
    return this.withAuth(async () => {
      const data = await this.post<{
        projects: {
          id: string;
          name: string;
          activities: { id: string; name: string; projectId: string }[];
        }[];
      }>(
        this.token,
        `query {
        projects {
          id name
          activities { id name projectId }
        }
      }`,
        {},
      );
      return data.projects.map((p) => ({
        id: p.id,
        name: p.name,
        activities: p.activities ?? [],
      }));
    });
  }

  async parseWorklog(
    text: string,
    dateFrom: string,
    dateTo: string,
  ): Promise<{ rows: ParsedRow[]; parser: string; warning: string | null }> {
    return this.withAuth(async () => {
      const data = await this.post<{
        parseWorklog: {
          parser: string;
          warning?: string | null;
          rows: {
            workDate: string;
            projectName: string;
            activityName: string;
            hours: number;
            description: string;
            confidence: string;
            projectId?: string | null;
            activityId?: string | null;
          }[];
        };
      }>(
        this.token,
        `mutation ($input: ParseWorklogInput!) {
        parseWorklog(input: $input) {
          parser
          warning
          rows {
            workDate projectName activityName hours description confidence projectId activityId
          }
        }
      }`,
        { input: { text, dateFrom, dateTo } },
      );

      const payload = data.parseWorklog;
      return {
        parser: payload.parser,
        warning: payload.warning ?? null,
        rows: payload.rows.map((r) => ({
          workDate: r.workDate,
          projectName: r.projectName,
          activityName: r.activityName,
          hours: r.hours,
          description: r.description,
          confidence: r.confidence ?? "",
          projectId: r.projectId ?? null,
          activityId: r.activityId ?? null,
        })),
      };
    });
  }

  async listWorklogs(from: string, to: string): Promise<WorklogInfo[]> {
    return this.withAuth(async () => {
      const data = await this.post<{
        worklogs: {
          id: string;
          workDate: string;
          projectName: string;
          hours: number;
          description: string;
        }[];
      }>(
        this.token,
        `query ($from: String, $to: String) {
        worklogs(from: $from, to: $to) {
          id workDate projectName hours description
        }
      }`,
        { from, to },
      );
      return data.worklogs ?? [];
    });
  }

  async saveWorklogs(items: SaveItem[]): Promise<{
    savedCount: number;
    destination: string;
    message: string | null;
  }> {
    return this.withAuth(async () => {
      const data = await this.post<{
        saveWorklogs: { savedCount: number; destination: string; message?: string | null };
      }>(
        this.token,
        `mutation ($input: SaveWorklogsInput!) {
        saveWorklogs(input: $input) {
          savedCount destination message
        }
      }`,
        { input: { items } },
      );
      return {
        savedCount: data.saveWorklogs.savedCount,
        destination: data.saveWorklogs.destination,
        message: data.saveWorklogs.message ?? null,
      };
    });
  }

  async listStickyNotes(dateFrom?: string, dateTo?: string) {
    return this.withAuth(async () => {
      const data = await this.post<{
        stickyNotes: { id: string; noteDate: string; body: string }[];
      }>(
        this.token,
        `query ($from: String, $to: String) {
        stickyNotes(from: $from, to: $to) { id noteDate body }
      }`,
        { from: dateFrom ?? null, to: dateTo ?? null },
      );
      return data.stickyNotes;
    });
  }

  async createStickyNote(noteDate: string, body: string) {
    return this.withAuth(async () => {
      const data = await this.post<{ createStickyNote: { id: string } }>(
        this.token,
        `mutation ($input: CreateStickyNoteInput!) {
        createStickyNote(input: $input) { id }
      }`,
        { input: { noteDate, body } },
      );
      return data.createStickyNote;
    });
  }

  async deleteStickyNote(id: string) {
    return this.withAuth(async () => {
      const data = await this.post<{ deleteStickyNote: boolean }>(
        this.token,
        `mutation ($id: UUID!) { deleteStickyNote(id: $id) }`,
        { id },
      );
      return data.deleteStickyNote;
    });
  }

  async draftFromSources(input: {
    dateFrom: string;
    dateTo: string;
    includeNotes?: boolean;
    includeGitlab?: boolean;
    includeMail?: boolean;
  }) {
    return this.withAuth(async () => {
      const data = await this.post<{
        draftWorklogFromSources: {
          parser: string;
          warning?: string | null;
          sourcesUsed: string;
          combinedPreview?: string | null;
          rows: ParsedRow[];
        };
      }>(
        this.token,
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
            dateFrom: input.dateFrom,
            dateTo: input.dateTo,
            includeNotes: input.includeNotes ?? true,
            includeGitlab: input.includeGitlab ?? false,
            includeMail: input.includeMail ?? false,
          },
        },
      );
      return data.draftWorklogFromSources;
    });
  }

  private async post<T>(
    token: string | null,
    query: string,
    variables: unknown,
  ): Promise<T> {
    const headers: Record<string, string> = {
      Accept: "application/json",
      "Content-Type": "application/json",
    };
    if (token) headers.Authorization = `Bearer ${token}`;

    let res: Response;
    try {
      res = await fetch(this.apiUrl, {
        method: "POST",
        headers,
        body: JSON.stringify({ query, variables }),
        signal: AbortSignal.timeout(120_000),
      });
    } catch (err) {
      throw new Error(
        `Cannot reach LogEase API at ${this.apiUrl}. ` +
          `Start it with: cd LogEase.Api && dotnet run\n` +
          `(${err instanceof Error ? err.message : String(err)})`,
      );
    }

    const body = (await res.json()) as {
      data?: T;
      errors?: { message?: string; extensions?: { message?: string } }[];
    };

    if (body.errors?.length) {
      const e = body.errors[0];
      const msg = e.extensions?.message ?? e.message ?? "GraphQL error";
      const code = (e as { extensions?: { code?: string } }).extensions?.code;
      const err = new Error(`LogEase API error: ${msg}`);
      (err as Error & { graphqlCode?: string }).graphqlCode = code;
      throw err;
    }
    if (!body.data) throw new Error("LogEase API returned no data");
    return body.data;
  }

  /** Run an authenticated call; on AUTH failures, re-login once and retry. */
  async withAuth<T>(fn: () => Promise<T>): Promise<T> {
    await this.ensureAuth();
    try {
      return await fn();
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      const code = e instanceof Error ? (e as Error & { graphqlCode?: string }).graphqlCode : undefined;
      const authFail =
        code === "AUTH_NOT_AUTHENTICATED" ||
        /not authorized|AUTH_NOT_AUTHENTICATED/i.test(msg);
      if (!authFail) throw e;
      log("Auth failed; re-login and retry once…");
      await this.ensureAuth(true);
      return await fn();
    }
  }
}

export function createClientFromEnv(): LogEaseClient {
  let apiUrl = (process.env.LOGEASE_API_URL ?? "http://127.0.0.1:5055/graphql").trim();
  // Windows often resolves localhost to ::1; prefer IPv4 loopback for local API.
  apiUrl = apiUrl.replace("://localhost:", "://127.0.0.1:");
  const email = (process.env.LOGEASE_EMAIL ?? "").trim();
  const password = process.env.LOGEASE_PASSWORD ?? "";

  if (!email || !password) {
    throw new Error(
      "Set LOGEASE_EMAIL and LOGEASE_PASSWORD in the MCP server env " +
        "(see mcp/cursor-mcp.example.json).",
    );
  }

  log(`API ${apiUrl} as ${email}`);
  return new LogEaseClient(apiUrl, email, password);
}
