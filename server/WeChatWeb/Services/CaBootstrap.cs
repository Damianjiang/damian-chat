using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace WeChatWeb.Services;

/// <summary>
/// 首次信任引导（Bootstrap）：给「还没有安装根证书」的客户端一个免警告的入口。
///
/// ★ 要解决的问题：
///   自签名证书第一次访问时，浏览器必然弹「连接不安全」。用户点「继续前往」虽然能进站，
///   但手机上根本没法把 .crt 文件方便地传到设备里 —— 而微信/QQ 内置浏览器即便装了
///   用户 CA 也不会信任（它们用自家网络栈），体验极差。
///   这里在**明文 HTTP 分支**直接吐出 CA 文件与安装引导页，客户端无需先信任证书就能拿到：
///     http://域名:端口/ca       → 分平台图文安装步骤
///     http://域名:端口/ca.crt   → 根证书本体（PEM）
///     http://域名:端口/install-ca.ps1 → Windows 一键安装（内嵌 base64，不依赖网络）
///
/// 安全边界：
///   • 只提供**公钥证书**，绝不触碰私钥；根证书本来就是公开信息，泄漏无风险。
///   • 内容在进程启动时一次性生成，之后只读，不解析任何请求参数（无注入面）。
///   • 仅在「证书文件存在」时启用；纯 HTTP 部署下整段为 no-op。
/// </summary>
public sealed class CaBootstrap
{
    private readonly Dictionary<string, (string ContentType, byte[] Body)> _routes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>没有任何可服务内容时为 false（调用方据此跳过整段逻辑）。</summary>
    public bool Enabled => _routes.Count > 0;

    public string? ThumbprintSha1 { get; private set; }
    public string? ThumbprintSha256 { get; private set; }
    public string? SubjectText { get; private set; }
    public string? NotAfterText { get; private set; }
    public string? SiteName { get; private set; }

    /// <summary>中间件用：路径 → (Content-Type, 响应体)。路径均为小写、不含 query。</summary>
    public bool TryGet (string path, out string contentType, out byte[] body)
    {
        if (_routes.TryGetValue(path, out var hit))
        {
            contentType = hit.ContentType;
            body = hit.Body;
            return true;
        }
        contentType = "";
        body = Array.Empty<byte>();
        return false;
    }

    /// <summary>引导页路径集合（用于明文跳转白名单，避免被 307 走）。</summary>
    public static readonly string[] Paths = { "/ca", "/ca.crt", "/install-ca.ps1" };

    /// <summary>
    /// 从磁盘上的 CA 证书构建引导内容。任何异常都吞掉并返回空实例 ——
    /// 引导页只是锦上添花，绝不能因为它让整站起不来。
    /// </summary>
    public static CaBootstrap Create (string? caPath, string? leafPath, ILogger? logger = null)
    {
        var result = new CaBootstrap();
        try
        {
            if (string.IsNullOrWhiteSpace(caPath) || !File.Exists(caPath)) return result;

            var ca = new X509Certificate2(caPath);
            result.ThumbprintSha1 = FormatThumbprint(ca.Thumbprint);
            result.ThumbprintSha256 = FormatThumbprint(ca.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256));
            result.SubjectText = ca.Subject;
            result.NotAfterText = ca.NotAfter.ToString("yyyy-MM-dd");
            result.SiteName = ReadSimpleName(leafPath) ?? "本站";

            var pem = ExportPem(ca);
            var pemBytes = Encoding.ASCII.GetBytes(pem);

            result._routes["/ca.crt"] = ("application/x-x509-ca-cert", pemBytes);
            result._routes["/ca"] = ("text/html; charset=utf-8", Encoding.UTF8.GetBytes(result.BuildPage()));
            result._routes["/install-ca.ps1"] = ("text/plain; charset=utf-8", Encoding.UTF8.GetBytes(result.BuildPowerShell(ca)));

            logger?.LogInformation(
                "[CA] 首次信任引导已启用：/ca、/ca.crt、/install-ca.ps1（根证书 {Thumbprint}，至 {NotAfter}）",
                result.ThumbprintSha1, result.NotAfterText);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[CA] 引导页生成失败（不影响主站）");
        }
        return result;
    }

    private static string? ReadSimpleName (string? leafPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(leafPath) || !File.Exists(leafPath)) return null;
            return new X509Certificate2(leafPath).GetNameInfo(X509NameType.SimpleName, false);
        }
        catch { return null; }
    }

    /// <summary>指纹按 aabbcc… → AA:BB:CC… 展示，方便人眼逐段核对。</summary>
    private static string FormatThumbprint (string hex)
    {
        hex = (hex ?? "").Replace(" ", "").ToUpperInvariant();
        var sb = new StringBuilder(hex.Length + hex.Length / 2);
        for (var i = 0; i < hex.Length; i += 2)
        {
            if (sb.Length > 0) sb.Append(':');
            sb.Append(hex, i, Math.Min(2, hex.Length - i));
        }
        return sb.ToString();
    }

    /// <summary>
    /// 导出 PEM。优先走 Base64 手工拼装 —— X509Certificate2.ExportCertificatePem() 在
    /// .NET 7+ 才有，这里手写一份，跨版本都稳，也保证行宽 64 的规范格式。
    /// </summary>
    private static string ExportPem (X509Certificate2 cert)
    {
        var raw = cert.RawData;
        var b64 = Convert.ToBase64String(raw);
        var sb = new StringBuilder(b64.Length + b64.Length / 64 + 64);
        sb.Append("-----BEGIN CERTIFICATE-----\n");
        for (var i = 0; i < b64.Length; i += 64)
            sb.Append(b64, i, Math.Min(64, b64.Length - i)).Append('\n');
        sb.Append("-----END CERTIFICATE-----\n");
        return sb.ToString();
    }

    private string BuildPowerShell (X509Certificate2 ca)
    {
        var b64 = Convert.ToBase64String(ca.RawData);
        var lines = new List<string>();
        for (var i = 0; i < b64.Length; i += 76)
            lines.Add(b64.Substring(i, Math.Min(76, b64.Length - i)));
        var b64Block = string.Join("\n", lines);

        return """
# ============================================================
#  tcraft 根证书一键安装（Windows）
#  以当前用户身份写入「受信任的根证书颁发机构」，无需管理员权限。
#  只安装公钥证书，不涉及任何私钥。
# ============================================================
$ErrorActionPreference = 'Stop'

$b64 = @'
__B64__
'@

$expected = '__SHA1__'

$raw  = [Convert]::FromBase64String(($b64 -replace '\s', ''))
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 (,$raw)

if ($cert.Thumbprint -ne $expected) {
    Write-Host "[x] 指纹不匹配，已中止（文件可能被篡改）" -ForegroundColor Red
    Write-Host "    期望: $expected"
    Write-Host "    实际: $($cert.Thumbprint)"
    Read-Host "按回车关闭"
    exit 1
}

Write-Host ""
Write-Host "  根证书: $($cert.Subject)"
Write-Host "  有效期: 至 $($cert.NotAfter.ToString('yyyy-MM-dd'))"
Write-Host "   SHA-1 : $($cert.Thumbprint)"
Write-Host ""

$store = New-Object System.Security.Cryptography.X509Certificates.X509Store ('Root', 'CurrentUser')
$store.Open('ReadWrite')
try {
    $hit = $store.Certificates.Find('FindByThumbprint', $cert.Thumbprint, $false)
    if ($hit.Count -gt 0) {
        Write-Host "[=] 已经安装过了，无需重复。" -ForegroundColor Yellow
    } else {
        $store.Add($cert)
        Write-Host "[+] 安装成功：当前用户 \ 受信任的根证书颁发机构" -ForegroundColor Green
    }
} finally {
    $store.Close()
}

Write-Host ""
Write-Host "请完全退出浏览器（含后台进程）后重新打开本站。" -ForegroundColor Cyan
Write-Host "Firefox 使用独立证书库，需另外在 设置→隐私→证书 中导入。"
Write-Host ""
Read-Host "按回车关闭"
""".Replace("__B64__", b64Block).Replace("__SHA1__", ca.Thumbprint ?? "");
    }

    private string BuildPage ()
    {
        var site = System.Net.WebUtility.HtmlEncode(SiteName ?? "本站");
        var sha1 = System.Net.WebUtility.HtmlEncode(ThumbprintSha1 ?? "");
        var sha256 = System.Net.WebUtility.HtmlEncode(ThumbprintSha256 ?? "");
        var subject = System.Net.WebUtility.HtmlEncode(SubjectText ?? "");
        var notAfter = System.Net.WebUtility.HtmlEncode(NotAfterText ?? "");

        return """
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
<title>安装根证书 · __SITE__</title>
<style>
  *{box-sizing:border-box;-webkit-tap-highlight-color:transparent}
  html,body{max-width:100%;overflow-x:hidden}
  body{margin:0;padding:0;background:#f2f3f5;color:#111;
       font:16px/1.65 -apple-system,BlinkMacSystemFont,"Segoe UI","PingFang SC","Microsoft YaHei",sans-serif}
  .wrap{max-width:640px;margin:0 auto;padding:20px 16px 48px}
  .card{background:#fff;border-radius:16px;padding:20px;margin-bottom:14px;
        box-shadow:0 1px 2px rgba(0,0,0,.04),0 8px 24px rgba(0,0,0,.04)}
  h1{font-size:21px;margin:0 0 6px;font-weight:650;letter-spacing:-.2px}
  .sub{color:#666;font-size:14px;margin:0 0 18px}
  .btn{display:flex;align-items:center;justify-content:center;gap:8px;
       width:100%;padding:15px;border-radius:12px;background:#07c160;color:#fff;
       font-size:16px;font-weight:600;text-decoration:none;border:0;
       box-shadow:0 4px 14px rgba(7,193,96,.28);transition:transform .12s}
  .btn:active{transform:scale(.985)}
  .btn.ghost{background:#f2f3f5;color:#111;box-shadow:none;margin-top:10px;font-weight:500}
  .meta{font-size:12.5px;color:#888;line-height:1.9;word-break:break-all;overflow-wrap:anywhere;
        background:#fafafa;border-radius:10px;padding:12px;margin-top:16px}
  .meta b{color:#555;font-weight:600}
  h2{font-size:15px;margin:0 0 10px;font-weight:650;display:flex;align-items:center;gap:8px}
  h2 .dot{width:6px;height:6px;border-radius:50%;background:#07c160;flex:0 0 auto}
  ol{margin:0;padding-left:20px}
  ol li{margin:7px 0;font-size:14.5px;color:#333}
  code{background:#f2f3f5;border-radius:5px;padding:1px 6px;font-size:13px;
       font-family:ui-monospace,Consolas,monospace}
  .warn{background:#fff8e6;border:1px solid #ffe1a3;border-radius:12px;
        padding:14px 16px;font-size:13.5px;color:#7a5a00;line-height:1.7}
  .warn b{color:#5c4300}
  .plat{display:flex;gap:8px;margin-bottom:14px;flex-wrap:wrap}
  .plat button{flex:1 1 0;min-width:88px;padding:9px 6px;border-radius:10px;border:1px solid #e3e3e3;
       background:#fff;font-size:14px;color:#444;font-family:inherit;cursor:pointer}
  .plat button.on{background:#07c160;border-color:#07c160;color:#fff;font-weight:600}
  .pane{display:none}.pane.on{display:block}
  .foot{text-align:center;color:#aaa;font-size:12px;margin-top:22px}
</style>
</head>
<body>
<div class="wrap">

  <div class="card">
    <h1>安装根证书</h1>
    <p class="sub">这台设备还没有信任 __SITE__ 的证书。装一次，之后打开本站不再有安全警告，也不会再弹「继续前往」。</p>
    <a class="btn" href="/ca.crt" download="tcraft-ca.crt">下载根证书</a>
    <a class="btn ghost" href="__ORIGIN__">我已经装好了，进入站点</a>
    <div class="meta">
      <b>证书主体</b>　__SUBJECT__<br>
      <b>有效期至</b>　__NOTAFTER__<br>
      <b>SHA-1</b>　__SHA1__<br>
      <b>SHA-256</b>　__SHA256__
    </div>
  </div>

  <div class="card">
    <div class="plat">
      <button class="on" data-p="ios">iPhone / iPad</button>
      <button data-p="android">Android</button>
      <button data-p="win">Windows</button>
    </div>

    <div class="pane on" id="p-ios">
      <h2><span class="dot"></span>iOS 安装步骤</h2>
      <ol>
        <li>用 <b>Safari</b> 打开本页（微信里请点右上角「···」→ 在 Safari 中打开）</li>
        <li>点上面的「下载根证书」，弹出「此网站正尝试下载一个配置描述文件」时点<b>允许</b></li>
        <li>打开 <b>设置</b>，顶部会出现「已下载描述文件」，点进去 → 右上角<b>安装</b> → 输入锁屏密码</li>
        <li>关键一步：<b>设置 → 通用 → 关于本机 → 证书信任设置</b>，把 <code>tcraft</code> 的开关<b>打开</b></li>
        <li>回到 Safari 刷新本站，警告消失</li>
      </ol>
    </div>

    <div class="pane" id="p-android">
      <h2><span class="dot"></span>Android 安装步骤</h2>
      <ol>
        <li>点上面的「下载根证书」，文件会存到「下载」目录</li>
        <li>打开 <b>设置</b> → <b>安全</b>（部分机型在「密码与安全」）→ <b>加密与凭据</b></li>
        <li>点 <b>安装证书</b> → <b>CA 证书</b> → 会提示「你的数据可能不安全」，点<b>仍然安装</b></li>
        <li>选择刚下载的 <code>tcraft-ca.crt</code></li>
        <li>回到浏览器刷新本站，警告消失</li>
      </ol>
      <p class="sub" style="margin-top:12px">小米 / OPPO / vivo 路径略有差异，搜设置里的「证书」即可找到入口。</p>
    </div>

    <div class="pane" id="p-win">
      <h2><span class="dot"></span>Windows 安装步骤</h2>
      <ol>
        <li>下载根证书后<b>双击</b> <code>tcraft-ca.crt</code></li>
        <li>点 <b>安装证书</b> → 存储位置选<b>当前用户</b> → 下一步</li>
        <li>选 <b>将所有的证书都放入下列存储</b> → 浏览 → <b>受信任的根证书颁发机构</b> → 确定</li>
        <li>下一步 → 完成 → 弹出安全警告时点<b>是</b></li>
        <li><b>完全退出浏览器</b>（含后台进程）后重新打开</li>
      </ol>
      <p class="sub" style="margin-top:12px">也可以用管理端「网站设置 → 一键安装证书」，或下载 <a href="/install-ca.ps1">install-ca.ps1</a> 右键「使用 PowerShell 运行」。</p>
    </div>
  </div>

  <div class="card warn">
    <b>装之前请核对指纹。</b>只在你自己的设备上安装，不要把这个链接转给不信任的人。
    根证书本身是公开信息（不含私钥），但把它装进信任库等于承认「这张 CA 签发的任何网站都可信」——
    所以请确认上面的 SHA-1 与你在服务器上看到的一致。
  </div>

  <div class="card warn">
    <b>微信 / QQ 内置浏览器不认用户证书。</b>Android 上尤其如此（它们用自己的网络栈）。
    装完证书后，请用系统浏览器（Safari / Chrome / Edge）访问本站；在微信里会一直提示不安全。
  </div>

  <div class="foot">tcraft · 自签名根证书引导页</div>
</div>

<script>
  document.querySelectorAll('.plat button').forEach(function (b) {
    b.addEventListener('click', function () {
      document.querySelectorAll('.plat button').forEach(function (x) { x.classList.remove('on'); });
      document.querySelectorAll('.pane').forEach(function (x) { x.classList.remove('on'); });
      b.classList.add('on');
      var p = document.getElementById('p-' + b.dataset.p);
      if (p) p.classList.add('on');
    });
  });
  // 按 UA 预选平台，省一次点击
  (function () {
    var ua = navigator.userAgent;
    var p = /iPhone|iPad|iPod/i.test(ua) ? 'ios' : (/Android/i.test(ua) ? 'android' : 'win');
    var btn = document.querySelector('.plat button[data-p="' + p + '"]');
    if (btn) btn.click();
  })();
</script>
</body>
</html>
"""
        .Replace("__SITE__", site)
        .Replace("__SUBJECT__", subject)
        .Replace("__NOTAFTER__", notAfter)
        .Replace("__SHA1__", sha1)
        .Replace("__SHA256__", sha256)
        .Replace("__ORIGIN__", "javascript:location.replace(location.protocol+'//'+location.host)");
    }
}
