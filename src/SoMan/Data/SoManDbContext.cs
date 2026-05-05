using System.IO;
using Microsoft.EntityFrameworkCore;
using SoMan.Models;

namespace SoMan.Data;

public class SoManDbContext : DbContext
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountCategory> AccountCategories => Set<AccountCategory>();
    public DbSet<AccountCategoryMap> AccountCategoryMaps => Set<AccountCategoryMap>();
    public DbSet<AccountLink> AccountLinks => Set<AccountLink>();
    public DbSet<ProxyConfig> ProxyConfigs => Set<ProxyConfig>();
    public DbSet<ActionTemplate> ActionTemplates => Set<ActionTemplate>();
    public DbSet<ActionStep> ActionSteps => Set<ActionStep>();
    public DbSet<ScheduledTask> ScheduledTasks => Set<ScheduledTask>();
    public DbSet<TaskExecution> TaskExecutions => Set<TaskExecution>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    public DbSet<TaskPreset> TaskPresets => Set<TaskPreset>();

    private readonly string _dbPath;

    public SoManDbContext()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var soManDir = Path.Combine(appData, "SoMan");
        Directory.CreateDirectory(soManDir);
        _dbPath = Path.Combine(soManDir, "soman.db");
    }

    public SoManDbContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Account
        modelBuilder.Entity<Account>(e =>
        {
            e.HasIndex(a => new { a.Platform, a.Username }).IsUnique();
            e.HasOne(a => a.ProxyConfig)
                .WithMany(p => p.Accounts)
                .HasForeignKey(a => a.ProxyConfigId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // AccountCategoryMap (many-to-many)
        modelBuilder.Entity<AccountCategoryMap>(e =>
        {
            e.HasKey(m => new { m.AccountId, m.AccountCategoryId });
            e.HasOne(m => m.Account)
                .WithMany(a => a.Categories)
                .HasForeignKey(m => m.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.AccountCategory)
                .WithMany(c => c.Accounts)
                .HasForeignKey(m => m.AccountCategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AccountLink
        modelBuilder.Entity<AccountLink>(e =>
        {
            e.HasOne(l => l.SourceAccount)
                .WithMany(a => a.LinksAsSource)
                .HasForeignKey(l => l.SourceAccountId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.TargetAccount)
                .WithMany(a => a.LinksAsTarget)
                .HasForeignKey(l => l.TargetAccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ActionStep ordering
        modelBuilder.Entity<ActionStep>(e =>
        {
            e.HasOne(s => s.ActionTemplate)
                .WithMany(t => t.Steps)
                .HasForeignKey(s => s.ActionTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ScheduledTask
        modelBuilder.Entity<ScheduledTask>(e =>
        {
            e.HasOne(s => s.ActionTemplate)
                .WithMany(t => t.ScheduledTasks)
                .HasForeignKey(s => s.ActionTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // TaskExecution
        modelBuilder.Entity<TaskExecution>(e =>
        {
            e.HasOne(t => t.ScheduledTask)
                .WithMany(s => s.Executions)
                .HasForeignKey(t => t.ScheduledTaskId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.Account)
                .WithMany()
                .HasForeignKey(t => t.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(t => t.ActionTemplate)
                .WithMany()
                .HasForeignKey(t => t.ActionTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ActivityLog
        modelBuilder.Entity<ActivityLog>(e =>
        {
            e.HasIndex(l => l.ExecutedAt);
            e.HasIndex(l => l.AccountId);
            e.HasOne(l => l.Account)
                .WithMany(a => a.ActivityLogs)
                .HasForeignKey(l => l.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AppSettings
        modelBuilder.Entity<AppSettings>(e =>
        {
            e.HasIndex(s => s.Key).IsUnique();
        });

        // TaskPreset
        modelBuilder.Entity<TaskPreset>(e =>
        {
            e.HasIndex(p => p.Name).IsUnique();
            e.HasOne(p => p.ActionTemplate)
                .WithMany()
                .HasForeignKey(p => p.ActionTemplateId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        SeedDefaults(modelBuilder);
    }

    private static void SeedDefaults(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppSettings>().HasData(
            new AppSettings { Id = 1, Key = "Theme", Value = "Dark" },
            new AppSettings { Id = 2, Key = "BrowserDefaultMode", Value = "Headless" },
            new AppSettings { Id = 3, Key = "BrowserEngine", Value = "Chromium" },
            new AppSettings { Id = 4, Key = "MaxConcurrent", Value = "auto" },
            new AppSettings { Id = 5, Key = "DelayBetweenActionsMinMs", Value = "3000" },
            new AppSettings { Id = 6, Key = "DelayBetweenActionsMaxMs", Value = "10000" },
            new AppSettings { Id = 7, Key = "DelayBetweenAccountsMinMs", Value = "5000" },
            new AppSettings { Id = 8, Key = "DelayBetweenAccountsMaxMs", Value = "15000" },
            new AppSettings { Id = 9, Key = "DelayJitterPercent", Value = "20" },
            new AppSettings { Id = 10, Key = "EnableHumanSimulation", Value = "true" },
            new AppSettings { Id = 11, Key = "MaxCpuPercent", Value = "85" },
            new AppSettings { Id = 12, Key = "MinFreeRamPercent", Value = "20" },
            new AppSettings { Id = 13, Key = "CriticalCpuPercent", Value = "95" },
            new AppSettings { Id = 14, Key = "CriticalFreeRamPercent", Value = "10" },
            new AppSettings { Id = 15, Key = "LogRetentionDays", Value = "30" },
            new AppSettings { Id = 16, Key = "StartWithWindows", Value = "false" },
            new AppSettings { Id = 17, Key = "MinimizeToTray", Value = "true" }
        );
    }
}
