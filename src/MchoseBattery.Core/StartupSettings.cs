using System.Text.Json;

namespace MchoseBattery.Core;

public sealed record StartupSettings(bool AutoStart = true, int RefreshSeconds = 30)
{
    public static StartupSettings Parse(string? json) => json is null ? new() :
        JsonSerializer.Deserialize<StartupSettings>(json) ?? throw new JsonException("設定內容不可為 null");
}
