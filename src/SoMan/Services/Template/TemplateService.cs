using Microsoft.EntityFrameworkCore;
using SoMan.Data;
using SoMan.Models;

namespace SoMan.Services.Template;

public interface ITemplateService
{
    Task<List<ActionTemplate>> GetAllAsync(Platform? platform = null);
    Task<ActionTemplate?> GetByIdAsync(int id);
    Task<ActionTemplate> CreateAsync(string name, Platform platform, string? description = null);
    Task UpdateAsync(ActionTemplate template);
    Task DeleteAsync(int id);
    Task<ActionTemplate> DuplicateAsync(int id);
    Task<ActionStep> AddStepAsync(int templateId, ActionType actionType, string parametersJson, int delayMinMs = 3000, int delayMaxMs = 10000);
    Task UpdateStepAsync(ActionStep step);
    Task DeleteStepAsync(int stepId);
    Task ReorderStepsAsync(int templateId, List<int> stepIdsInOrder);
}

public class TemplateService : ITemplateService
{
    private static SoManDbContext CreateDb() => new();

    public async Task<List<ActionTemplate>> GetAllAsync(Platform? platform = null)
    {
        using var db = CreateDb();
        var query = db.ActionTemplates.Include(t => t.Steps.OrderBy(s => s.Order)).AsQueryable();
        if (platform.HasValue)
            query = query.Where(t => t.Platform == platform.Value);
        return await query.OrderByDescending(t => t.UpdatedAt).ToListAsync();
    }

    public async Task<ActionTemplate?> GetByIdAsync(int id)
    {
        using var db = CreateDb();
        return await db.ActionTemplates
            .Include(t => t.Steps.OrderBy(s => s.Order))
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<ActionTemplate> CreateAsync(string name, Platform platform, string? description = null)
    {
        using var db = CreateDb();
        var template = new ActionTemplate
        {
            Name = name,
            Platform = platform,
            Description = description,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.ActionTemplates.Add(template);
        await db.SaveChangesAsync();
        return template;
    }

    public async Task UpdateAsync(ActionTemplate template)
    {
        using var db = CreateDb();
        var existing = await db.ActionTemplates.FindAsync(template.Id);
        if (existing == null) return;
        existing.Name = template.Name;
        existing.Platform = template.Platform;
        existing.Description = template.Description;
        existing.IsActive = template.IsActive;
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        using var db = CreateDb();
        var template = await db.ActionTemplates.FindAsync(id);
        if (template == null) return;
        db.ActionTemplates.Remove(template);
        await db.SaveChangesAsync();
    }

    public async Task<ActionTemplate> DuplicateAsync(int id)
    {
        using var db = CreateDb();
        var original = await db.ActionTemplates
            .Include(t => t.Steps)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (original == null)
            throw new InvalidOperationException("Template not found.");

        var copy = new ActionTemplate
        {
            Name = $"{original.Name} (Copy)",
            Platform = original.Platform,
            Description = original.Description,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.ActionTemplates.Add(copy);
        await db.SaveChangesAsync();

        foreach (var step in original.Steps.OrderBy(s => s.Order))
        {
            db.ActionSteps.Add(new ActionStep
            {
                ActionTemplateId = copy.Id,
                Order = step.Order,
                ActionType = step.ActionType,
                ParametersJson = step.ParametersJson,
                DelayMinMs = step.DelayMinMs,
                DelayMaxMs = step.DelayMaxMs
            });
        }
        await db.SaveChangesAsync();

        return copy;
    }

    public async Task<ActionStep> AddStepAsync(int templateId, ActionType actionType, string parametersJson, int delayMinMs = 3000, int delayMaxMs = 10000)
    {
        using var db = CreateDb();
        int maxOrder = await db.ActionSteps
            .Where(s => s.ActionTemplateId == templateId)
            .Select(s => (int?)s.Order)
            .MaxAsync() ?? 0;

        var step = new ActionStep
        {
            ActionTemplateId = templateId,
            ActionType = actionType,
            ParametersJson = parametersJson,
            DelayMinMs = delayMinMs,
            DelayMaxMs = delayMaxMs,
            Order = maxOrder + 1
        };
        db.ActionSteps.Add(step);
        await db.SaveChangesAsync();

        // Update template timestamp
        var template = await db.ActionTemplates.FindAsync(templateId);
        if (template != null)
        {
            template.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        return step;
    }

    public async Task UpdateStepAsync(ActionStep step)
    {
        using var db = CreateDb();
        var existing = await db.ActionSteps.FindAsync(step.Id);
        if (existing == null) return;
        existing.ActionType = step.ActionType;
        existing.ParametersJson = step.ParametersJson;
        existing.DelayMinMs = step.DelayMinMs;
        existing.DelayMaxMs = step.DelayMaxMs;
        existing.Order = step.Order;
        await db.SaveChangesAsync();
    }

    public async Task DeleteStepAsync(int stepId)
    {
        using var db = CreateDb();
        var step = await db.ActionSteps.FindAsync(stepId);
        if (step == null) return;
        int templateId = step.ActionTemplateId;
        db.ActionSteps.Remove(step);
        await db.SaveChangesAsync();

        // Re-order remaining steps
        var remaining = await db.ActionSteps
            .Where(s => s.ActionTemplateId == templateId)
            .OrderBy(s => s.Order)
            .ToListAsync();
        for (int i = 0; i < remaining.Count; i++)
            remaining[i].Order = i + 1;
        await db.SaveChangesAsync();
    }

    public async Task ReorderStepsAsync(int templateId, List<int> stepIdsInOrder)
    {
        using var db = CreateDb();
        var steps = await db.ActionSteps
            .Where(s => s.ActionTemplateId == templateId)
            .ToListAsync();

        for (int i = 0; i < stepIdsInOrder.Count; i++)
        {
            var step = steps.FirstOrDefault(s => s.Id == stepIdsInOrder[i]);
            if (step != null) step.Order = i + 1;
        }
        await db.SaveChangesAsync();
    }
}
