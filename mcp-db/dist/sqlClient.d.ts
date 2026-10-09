export declare class SqlClient {
    private readonly connectionString;
    private pool;
    constructor(connectionString: string);
    connect(): Promise<void>;
    close(): Promise<void>;
    listLogEaseTables(): Promise<{
        schema: string;
        name: string;
        rows: number | null;
    }[]>;
    describeTable(tableName: string): Promise<{
        table: string;
        columns: {
            name: string;
            type: string;
            nullable: boolean;
            maxLength: number | null;
        }[];
    }>;
    runSelect(query: string, maxRows?: number): Promise<{
        rowCount: number;
        columns: string[];
        rows: Record<string, unknown>[];
        note: string;
    }>;
}
export declare function createSqlClientFromEnv(): SqlClient;
export declare const DEMO_QUERIES: {
    worklogHoursByProject: (days: number) => string;
    recentAiParseEvents: (limit: number) => string;
    stickyNotesThisMonth: () => string;
    openOpsIncidents: () => string;
};
