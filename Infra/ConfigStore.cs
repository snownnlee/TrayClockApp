using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using TrayClockApp.Core;

namespace TrayClockApp.Infra;

public static class ConfigStore
{
    public static JsonObject? Load()
    {
        if (!File.Exists(Constants.ConfigFile)) return null;
        try
        {
            var json = File.ReadAllText(Constants.ConfigFile);
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"加载配置失败: {ex.Message}");
            return null;
        }
    }

    public static void Save(JsonObject config)
    {
        try
        {
            Directory.CreateDirectory(Constants.ConfigDir);
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(Constants.ConfigFile, config.ToJsonString(options));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"保存配置失败: {ex.Message}");
        }
    }
}