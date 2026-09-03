using System.Diagnostics;
using System.Net.Http;

namespace Zapret2Ultra.Services;

public sealed record StrategyProbeResult(string Strategy, int Successes, double AverageLatencyMs, string Details);

public sealed class AutoStrategyService
{
    private static readonly string[] ProbeUrls =
    {
        "https://www.google.com/generate_204",
        "https://www.youtube.com/generate_204",
        "https://discord.com/api/v9/experiments",
    };

    public async Task<StrategyProbeResult[]> ProbeAsync(EngineService engine, CancellationToken cancellationToken = default)
    {
        var results = new List<StrategyProbeResult>();
        foreach (var strategy in new[] { "balanced", "aggressive" })
        {
            await engine.StartAsync(strategy, cancellationToken);
            try
            {
                var successes = 0;
                var latencies = new List<double>();
                foreach (var url in ProbeUrls)
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(7) };
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        using var response = await client.GetAsync(url, cancellationToken);
                        sw.Stop();
                        if ((int)response.StatusCode < 500) { successes++; latencies.Add(sw.Elapsed.TotalMilliseconds); }
                    }
                    catch { sw.Stop(); }
                }
                results.Add(new StrategyProbeResult(strategy, successes, latencies.Count == 0 ? double.MaxValue : latencies.Average(), $"{successes}/{ProbeUrls.Length} OK"));
            }
            finally { await engine.StopAsync(); }
        }
        return results.OrderByDescending(x => x.Successes).ThenBy(x => x.AverageLatencyMs).ToArray();
    }
}
