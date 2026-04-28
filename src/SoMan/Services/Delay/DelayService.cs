namespace SoMan.Services.Delay;

public interface IDelayService
{
    Task WaitAsync(int minMs, int maxMs, CancellationToken ct = default);
    Task WaitBetweenAccountsAsync(CancellationToken ct = default);
    int GetRandomDelay(int minMs, int maxMs);
}

public class DelayService : IDelayService
{
    private readonly Random _random = new();
    private readonly int _betweenAccountsMinMs;
    private readonly int _betweenAccountsMaxMs;
    private readonly int _jitterPercent;
    private readonly bool _humanSimulation;

    public DelayService(
        int betweenAccountsMinMs = 5000,
        int betweenAccountsMaxMs = 15000,
        int jitterPercent = 20,
        bool humanSimulation = true)
    {
        _betweenAccountsMinMs = betweenAccountsMinMs;
        _betweenAccountsMaxMs = betweenAccountsMaxMs;
        _jitterPercent = jitterPercent;
        _humanSimulation = humanSimulation;
    }

    public async Task WaitAsync(int minMs, int maxMs, CancellationToken ct = default)
    {
        var delay = GetRandomDelay(minMs, maxMs);
        await Task.Delay(delay, ct);
    }

    public async Task WaitBetweenAccountsAsync(CancellationToken ct = default)
    {
        var delay = GetRandomDelay(_betweenAccountsMinMs, _betweenAccountsMaxMs);
        await Task.Delay(delay, ct);
    }

    public int GetRandomDelay(int minMs, int maxMs)
    {
        if (minMs >= maxMs) return minMs;

        int baseDelay;

        if (_humanSimulation)
        {
            // Normal distribution (Box-Muller transform) - more human-like
            double mean = (minMs + maxMs) / 2.0;
            double stdDev = (maxMs - minMs) / 6.0; // 99.7% within range

            double u1 = 1.0 - _random.NextDouble();
            double u2 = 1.0 - _random.NextDouble();
            double normal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);

            baseDelay = (int)(mean + stdDev * normal);

            // Occasional long pause (simulating distraction, 5% chance)
            if (_random.NextDouble() < 0.05)
            {
                baseDelay = (int)(baseDelay * (1.5 + _random.NextDouble()));
            }
        }
        else
        {
            baseDelay = _random.Next(minMs, maxMs);
        }

        // Apply jitter
        if (_jitterPercent > 0)
        {
            double jitter = baseDelay * (_jitterPercent / 100.0);
            baseDelay += (int)(_random.NextDouble() * jitter * 2 - jitter);
        }

        return Math.Max(minMs / 2, Math.Min(baseDelay, maxMs * 2));
    }
}
