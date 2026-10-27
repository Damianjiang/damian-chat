using System.Text.Json;

namespace WxAdmin;

public static class Paths
{
    public static string WxAdminDir { get; } = FindWxAdminDir();
    public static string ServerDir { get; } = Directory.GetParent(WxAdminDir)!.FullName;
    public static string WebProject { get; } = Path.Combine(ServerDir, "WeChatWeb");
    public static string WebDll { get; } = Path.Combine(WebProject, "bin", "Debug", "net8.0", "WeChatWeb.dll");
    public static string LogDir { get; } = Path.Combine(WebProject, "logs");
    public static string ConfigFile { get; } = Path.Combine(WxAdminDir, "wxadmin.json");
    public static string PidFile { get; } = Path.Combine(WxAdminDir, ".server.pid");

    /// <summary>从 exe 输出目录（bin/Debug/net8.0）上溯 3 级到 WxAdmin 源码目录。</summary>
    private static string FindWxAdminDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 3 && dir != null; i++)
        {
            dir = dir.Parent;
        }
        return dir!.FullName;
    }
}

public sealed class WxConfig
{
    public int Port { get; set; } = 5000;

    public static WxConfig Load()
    {
        if (File.Exists(Paths.ConfigFile))
        {
            var json = File.ReadAllText(Paths.ConfigFile);
            var cfg = JsonSerializer.Deserialize<WxConfig>(json);
            if (cfg != null) return cfg;
        }
        var def = new WxConfig();
        Save(def);
        return def;
    }

    public static void Save(WxConfig cfg)
    {
        File.WriteAllText(Paths.ConfigFile, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
    }
}
