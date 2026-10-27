namespace WxAdmin.Shared;

/// <summary>
/// 管理密钥读取：本机管理端（WxAdmin 控制台 / WxAdminGui）直接从服务端生成的密钥文件读，
/// 不需要用户输入任何账号密码。密钥文件只在本机，服务端管理接口也只接受 loopback 请求。
/// </summary>
public static class AdminKeyFile
{
    /// <summary>服务端密钥文件路径（server/WeChatWeb/data/admin.key）。</summary>
    public static string Path
    {
        get
        {
            // 本工具（WxAdminGui / WxAdmin）exe 位于 server/<tool>/bin/Debug/netX/。
            // WeChatWeb 是 server 下的「兄弟」项目目录，不是本工具目录的子目录，
            // 因此必须上溯 4 级到 server 再拼 WeChatWeb（此前只上溯 3 级，算出的
            // <tool>/WeChatWeb 不存在 → 读不到 key → 管理接口 401）。
            var server = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            return System.IO.Path.Combine(server, "WeChatWeb", "data", "admin.key");
        }
    }

    /// <summary>读取密钥；不存在返回 null（服务未启动过）。</summary>
    public static string? Read ()
    {
        try
        {
            if (!System.IO.File.Exists(Path)) return null;
            var key = System.IO.File.ReadAllText(Path).Trim();
            return key.Length >= 32 ? key : null;
        }
        catch
        {
            return null;
        }
    }
}