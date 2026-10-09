using System.Text;
using LogEase.Api.Data;
using LogEase.Api.GraphQL;
using LogEase.Api.Integrations.Gitlab;
using LogEase.Api.Integrations.Kimai;
using LogEase.Api.Integrations.Llm;
using LogEase.Api.Integrations.Mail;
using LogEase.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<LogEaseDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpClient();
builder.Services.AddScoped<ILlmChatClient, ChainedLlmChatClient>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AzureAdAuthService>();
builder.Services.AddScoped<WorklogParseService>();
builder.Services.AddScoped<ProjectCatalogService>();
builder.Services.AddScoped<KimaiApiClient>();
builder.Services.AddScoped<KimaiTimesheetService>();
builder.Services.AddScoped<IKimaiTimesheetAdapter, KimaiTimesheetAdapter>();
builder.Services.AddScoped<WorklogStorageSettingsService>();
builder.Services.AddScoped<GitlabActivityClient>();
builder.Services.AddScoped<MailActivityClient>();
builder.Services.AddScoped<WorklogDraftFromSourcesService>();
builder.Services.AddScoped<AiOpsService>();
builder.Services.AddScoped<AutoTestService>();

var jwtKey = builder.Configuration["Jwt:Key"] ?? "LogEase-Dev-Secret-Key-Change-Me-32chars!";
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        };
    });
builder.Services.AddAuthorization();

builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .AddAuthorization()
    .ModifyRequestOptions(opt =>
    {
        // Save/parse can talk to Kimai + SQL; default HotChocolate 30s (HC0045) is too short.
        opt.ExecutionTimeout = TimeSpan.FromMinutes(3);
        // Surface real exception text instead of HotChocolate "Unexpected Execution Error".
        opt.IncludeExceptionDetails = builder.Environment.IsDevelopment()
            || builder.Configuration.GetValue("GraphQL:IncludeExceptionDetails", true);
    });

builder.Services.AddCors(opt =>
{
    opt.AddDefaultPolicy(p =>
        p.WithOrigins(
                "http://localhost:3000",
                "http://localhost:3001",
                "http://localhost:3002",
                "http://localhost:3003",
                "http://127.0.0.1:3000",
                "http://127.0.0.1:3001",
                "http://127.0.0.1:3002")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LogEaseDbContext>();
    // Ensure Customize-AI column exists on shared DBs that already had logease_Users.
    try
    {
        await db.Database.ExecuteSqlRawAsync("""
            IF COL_LENGTH(N'dbo.logease_Users', N'AiParseInstructions') IS NULL
                ALTER TABLE [dbo].[logease_Users] ADD [AiParseInstructions] NVARCHAR(4000) NULL;
            IF COL_LENGTH(N'dbo.logease_Users', N'AzureObjectId') IS NULL
                ALTER TABLE [dbo].[logease_Users] ADD [AzureObjectId] NVARCHAR(64) NULL;
            IF COL_LENGTH(N'dbo.logease_Users', N'ProfilePhotoUrl') IS NULL
                ALTER TABLE [dbo].[logease_Users] ADD [ProfilePhotoUrl] NVARCHAR(MAX) NULL;
            ELSE
                ALTER TABLE [dbo].[logease_Users] ALTER COLUMN [ProfilePhotoUrl] NVARCHAR(MAX) NULL;
            IF COL_LENGTH(N'dbo.logease_Users', N'GitlabPersonalAccessToken') IS NULL
                ALTER TABLE [dbo].[logease_Users] ADD [GitlabPersonalAccessToken] NVARCHAR(512) NULL;
            IF COL_LENGTH(N'dbo.logease_Users', N'GitlabBaseUrl') IS NULL
                ALTER TABLE [dbo].[logease_Users] ADD [GitlabBaseUrl] NVARCHAR(200) NULL;
            IF COL_LENGTH(N'dbo.logease_Users', N'GitlabUsername') IS NULL
                ALTER TABLE [dbo].[logease_Users] ADD [GitlabUsername] NVARCHAR(120) NULL;
            IF COL_LENGTH(N'dbo.logease_Users', N'MailImapHost') IS NULL
                ALTER TABLE [dbo].[logease_Users] ADD [MailImapHost] NVARCHAR(200) NULL;
            IF COL_LENGTH(N'dbo.logease_Users', N'MailImapPort') IS NULL
                ALTER TABLE [dbo].[logease_Users] ADD [MailImapPort] INT NULL;
            IF COL_LENGTH(N'dbo.logease_Users', N'MailImapUser') IS NULL
                ALTER TABLE [dbo].[logease_Users] ADD [MailImapUser] NVARCHAR(200) NULL;
            IF COL_LENGTH(N'dbo.logease_Users', N'MailImapPassword') IS NULL
                ALTER TABLE [dbo].[logease_Users] ADD [MailImapPassword] NVARCHAR(512) NULL;
            IF COL_LENGTH(N'dbo.logease_Users', N'MailImapUseSsl') IS NULL
                ALTER TABLE [dbo].[logease_Users] ADD [MailImapUseSsl] BIT NOT NULL CONSTRAINT [DF_logease_Users_MailImapUseSsl] DEFAULT (1);
            IF OBJECT_ID(N'dbo.logease_StickyNotes', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[logease_StickyNotes] (
                    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [UserId] UNIQUEIDENTIFIER NOT NULL,
                    [NoteDate] DATE NOT NULL,
                    [Body] NVARCHAR(4000) NOT NULL,
                    [CreatedAt] DATETIME2 NOT NULL,
                    [UpdatedAt] DATETIME2 NOT NULL,
                    CONSTRAINT [FK_logease_StickyNotes_Users] FOREIGN KEY ([UserId])
                        REFERENCES [dbo].[logease_Users]([Id]) ON DELETE CASCADE
                );
                CREATE INDEX [IX_logease_StickyNotes_UserId_NoteDate]
                    ON [dbo].[logease_StickyNotes]([UserId], [NoteDate]);
            END
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'IX_logease_Users_AzureObjectId'
                  AND object_id = OBJECT_ID(N'dbo.logease_Users'))
                CREATE UNIQUE NONCLUSTERED INDEX [IX_logease_Users_AzureObjectId]
                    ON [dbo].[logease_Users]([AzureObjectId])
                    WHERE [AzureObjectId] IS NOT NULL;

            IF OBJECT_ID(N'dbo.logease_AiParseEvents', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[logease_AiParseEvents] (
                    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_logease_AiParseEvents] PRIMARY KEY,
                    [UserId] UNIQUEIDENTIFIER NULL,
                    [Parser] NVARCHAR(80) NOT NULL,
                    [RowCount] INT NOT NULL,
                    [DurationMs] INT NOT NULL,
                    [UsedFallback] BIT NOT NULL,
                    [Success] BIT NOT NULL,
                    [Warning] NVARCHAR(500) NULL,
                    [Source] NVARCHAR(40) NOT NULL,
                    [CreatedAt] DATETIME2(7) NOT NULL
                );
                CREATE INDEX [IX_logease_AiParseEvents_CreatedAt]
                    ON [dbo].[logease_AiParseEvents]([CreatedAt]);
                CREATE INDEX [IX_logease_AiParseEvents_Parser]
                    ON [dbo].[logease_AiParseEvents]([Parser]);
            END

            IF OBJECT_ID(N'dbo.logease_OpsIncidents', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[logease_OpsIncidents] (
                    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_logease_OpsIncidents] PRIMARY KEY,
                    [Title] NVARCHAR(200) NOT NULL,
                    [Detail] NVARCHAR(2000) NOT NULL,
                    [Severity] NVARCHAR(20) NOT NULL,
                    [Status] NVARCHAR(30) NOT NULL,
                    [Category] NVARCHAR(40) NOT NULL,
                    [RelatedParseEventId] UNIQUEIDENTIFIER NULL,
                    [CreatedByUserId] UNIQUEIDENTIFIER NULL,
                    [CreatedAt] DATETIME2(7) NOT NULL,
                    [UpdatedAt] DATETIME2(7) NOT NULL,
                    [ResolvedAt] DATETIME2(7) NULL
                );
                CREATE INDEX [IX_logease_OpsIncidents_Status]
                    ON [dbo].[logease_OpsIncidents]([Status]);
                CREATE INDEX [IX_logease_OpsIncidents_CreatedAt]
                    ON [dbo].[logease_OpsIncidents]([CreatedAt]);
            END

            IF OBJECT_ID(N'dbo.logease_TechDebtItems', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[logease_TechDebtItems] (
                    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_logease_TechDebtItems] PRIMARY KEY,
                    [Title] NVARCHAR(200) NOT NULL,
                    [Description] NVARCHAR(2000) NOT NULL,
                    [Area] NVARCHAR(40) NOT NULL,
                    [Priority] NVARCHAR(20) NOT NULL,
                    [Status] NVARCHAR(30) NOT NULL,
                    [CreatedAt] DATETIME2(7) NOT NULL,
                    [UpdatedAt] DATETIME2(7) NOT NULL
                );
                CREATE INDEX [IX_logease_TechDebtItems_Status]
                    ON [dbo].[logease_TechDebtItems]([Status]);
            END
            """);
        app.Logger.LogInformation("Ensured Azure / sticky-note / AI Ops tables on LogEase.");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex,
            "Could not ensure Azure columns on logease_Users. Azure login may fail until columns exist.");
    }

    await DbSeeder.SeedAsync(db, app.Configuration);
    try
    {
        var ops = scope.ServiceProvider.GetRequiredService<AiOpsService>();
        await ops.EnsureSeedDebtAsync(CancellationToken.None);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Could not seed technical debt items.");
    }
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    app = "LogEase.Api",
    graphql = "/graphql",
    health = "query { health }",
}));

app.MapGraphQL();

app.Run();
