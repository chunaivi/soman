using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SoMan.Data;
using SoMan.Models;

namespace SoMan.Services.Template;

public interface ITaskPresetService
{
    Task<List<TaskPreset>> GetAllAsync();
    Task<TaskPreset?> GetAsync(int id);
    Task<TaskPreset> SaveAsync(string name, int? templateId, IEnumerable<int> accountIds);
    Task<bool> RenameAsync(int id, string newName);
    Task DeleteAsync(int id);
    List<int> ParseAccountIds(TaskPreset preset);
}

public class TaskPresetService : ITaskPresetService
{
    private static SoManDbContext CreateDb() => new();

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<List<TaskPreset>> GetAllAsync()
    {
        using var db = CreateDb();
        return await db.TaskPresets
            .Include(p => p.ActionTemplate)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<TaskPreset?> GetAsync(int id)
    {
        using var db = CreateDb();
        return await db.TaskPresets
            .Include(p => p.ActionTemplate)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<TaskPreset> SaveAsync(string name, int? templateId, IEnumerable<int> accountIds)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Preset name cannot be empty.", nameof(name));

        var trimmedName = name.Trim();
        var idsJson = JsonSerializer.Serialize(accountIds.Distinct().OrderBy(x => x).ToList(), _jsonOpts);

        using var db = CreateDb();

        var existing = await db.TaskPresets.FirstOrDefaultAsync(p => p.Name == trimmedName);
        if (existing != null)
        {
            existing.ActionTemplateId = templateId;
            existing.AccountIdsJson = idsJson;
            existing.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return existing;
        }

        var preset = new TaskPreset
        {
            Name = trimmedName,
            ActionTemplateId = templateId,
            AccountIdsJson = idsJson,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.TaskPresets.Add(preset);
        await db.SaveChangesAsync();
        return preset;
    }

    public async Task<bool> RenameAsync(int id, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return false;
        var trimmed = newName.Trim();

        using var db = CreateDb();
        var preset = await db.TaskPresets.FindAsync(id);
        if (preset == null) return false;

        var conflict = await db.TaskPresets
            .AnyAsync(p => p.Name == trimmed && p.Id != id);
        if (conflict) return false;

        preset.Name = trimmed;
        preset.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task DeleteAsync(int id)
    {
        using var db = CreateDb();
        var preset = await db.TaskPresets.FindAsync(id);
        if (preset != null)
        {
            db.TaskPresets.Remove(preset);
            await db.SaveChangesAsync();
        }
    }

    public List<int> ParseAccountIds(TaskPreset preset)
    {
        if (string.IsNullOrWhiteSpace(preset.AccountIdsJson)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<int>>(preset.AccountIdsJson, _jsonOpts) ?? new();
        }
        catch
        {
            return new();
        }
    }
}
