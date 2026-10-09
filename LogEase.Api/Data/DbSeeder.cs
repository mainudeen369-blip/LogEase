using LogEase.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace LogEase.Api.Data;

public static class DbSeeder
{
    /// <summary>
    /// Prefer running Scripts/SqlServer_All.sql on the test DB.
    /// EnsureCreated is off by default (shared DBs). Optional seed if tables exist but empty.
    /// </summary>
    public static async Task SeedAsync(LogEaseDbContext db, IConfiguration config)
    {
        if (config.GetValue("Database:UseEnsureCreated", false))
        {
            await db.Database.EnsureCreatedAsync();
        }

        try
        {
            if (await db.Users.AnyAsync()) return;
        }
        catch
        {
            // Schema missing — run Scripts/SqlServer_All.sql first.
            return;
        }

        var user = new User
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Email = "demo@logease.local",
            Name = "Demo User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Demo@123"),
            ReminderTime = new TimeOnly(17, 45),
            ReminderEnabled = true,
            TimeZoneId = "India Standard Time",
        };

        var projects = new[]
        {
            MakeProject(Guid.Parse("22222222-2222-2222-2222-222222222201"), "Perception Survey",
                ("33333333-3333-3333-3333-333333333101", "Development"),
                ("33333333-3333-3333-3333-333333333102", "Bug Fix"),
                ("33333333-3333-3333-3333-333333333103", "Meeting")),
            MakeProject(Guid.Parse("22222222-2222-2222-2222-222222222202"), "Code Sharing Tool",
                ("33333333-3333-3333-3333-333333333201", "Development"),
                ("33333333-3333-3333-3333-333333333202", "Support"),
                ("33333333-3333-3333-3333-333333333203", "Meeting")),
            MakeProject(Guid.Parse("22222222-2222-2222-2222-222222222203"), "LogEase",
                ("33333333-3333-3333-3333-333333333301", "Development"),
                ("33333333-3333-3333-3333-333333333302", "Demo Prep"),
                ("33333333-3333-3333-3333-333333333303", "Meeting")),
            MakeProject(Guid.Parse("22222222-2222-2222-2222-222222222204"), "Internal Support",
                ("33333333-3333-3333-3333-333333333401", "Support"),
                ("33333333-3333-3333-3333-333333333402", "Documentation"),
                ("33333333-3333-3333-3333-333333333403", "Meeting")),
            MakeProject(Guid.Parse("22222222-2222-2222-2222-222222222205"), "Training / Learning",
                ("33333333-3333-3333-3333-333333333501", "Learning"),
                ("33333333-3333-3333-3333-333333333502", "Documentation"),
                ("33333333-3333-3333-3333-333333333503", "Meeting")),
            MakeProject(Guid.Parse("22222222-2222-2222-2222-222222222206"), "Company Announcement",
                ("33333333-3333-3333-3333-333333333601", "Development"),
                ("33333333-3333-3333-3333-333333333602", "Bug Fix"),
                ("33333333-3333-3333-3333-333333333603", "Support")),
        };

        db.Users.Add(user);
        db.Projects.AddRange(projects);
        await db.SaveChangesAsync();
    }

    private static Project MakeProject(Guid projectId, string name, params (string Id, string Name)[] activities)
    {
        return new Project
        {
            Id = projectId,
            Name = name,
            IsActive = true,
            Activities = activities.Select(a => new Activity
            {
                Id = Guid.Parse(a.Id),
                ProjectId = projectId,
                Name = a.Name,
            }).ToList(),
        };
    }
}
