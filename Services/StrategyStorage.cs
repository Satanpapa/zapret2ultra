using System.IO;
using System.Text.Json;

namespace Zapret2Ultra.Services;

public sealed record StoredStrategy(string Name, string EngineProfile, string Description);

public static class StrategyStorage
{
    public static async Task<IReadOnlyList<StoredStrategy>> LoadAsync(string directory, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory)) return Array.Empty<StoredStrategy>();
        var result = new List<StoredStrategy>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            await using var stream = File.OpenRead(file);
            try
            {
                var model = await JsonSerializer.DeserializeAsync<StoredStrategy>(stream, cancellationToken: cancellationToken);
                if (model is not null) result.Add(model);
            }
            catch (JsonException) { }
        }
        return result;
    }
}
