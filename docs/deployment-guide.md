# Deployment & Usage Guide · 部署与使用指南

This guide covers going **from zero to live**: one-machine self-hosting, Docker containers,
HTTPS, the admin GUI, and day-to-day ops + troubleshooting.

# 从零到上线：部署与运维指南

这份文档覆盖**从零到上线**的全过程：单机自托管、Docker 容器、HTTPS 加密、管理端 GUI、
常见问题排查。读完即可独立完成部署与日常运维。

> Related: [README.md](../README.md) (features, security, quick start) · [Releases](https://github.com/Damianjiang/damian-chat/releases)

---

# EN

## 1. What it does / 它能做什么

- **WeChat-style chat**: realtime 1v1 / group, recall, quote, save, delete, pat-a-pat, @mention, group announcement + confirmation.
- **QQ-style group management**: join by group number (open / question / approve / closed four policies), mute-all, per-member timed mute, group polls.
- **Moments / 朋友圈**: post text & media, like, comment.
- **Rich media**: image / voice / file; multi-device IndexedDB cache + offline fallback.
- **Account system**: email-code registration (SMTP) or direct account; WeChat-style "大面号" (random ID unsearchable by default, anti mass-add).
- **Deep security hardening (built-in)**: traffic firewall + auto-ban, dual captcha, httpOnly refresh tokens, isolated web honeypot, loopback + key-gated admin API, sensitive-word filter.
- **Admin GUI** (Windows WPF tray): start/stop service, change port, view online users, tweak security policy, manage users, read logs / feedback.

Data stays 100% on your machine. No cloud account, no telemetry.

## 2. Before you deploy / 部署前准备

| Need | Notes |
|---|---|
| OS | Backend is cross-platform; **admin GUI is Windows-only** (WPF). On Linux / macOS use Docker or run the backend directly |
| .NET SDK 8 | Confirm with `dotnet --version`. Required to build the admin GUI / backend |
| Browser | Chrome / Edge / Safari / modern Firefox |
| Port | Default `5000`; changeable (admin GUI / `--urls` / `ASPNETCORE_URLS`) |

> On first run, `server/WeChatWeb/data/` is created with locally generated keys and data (git-ignored, never uploaded).

## 3. Option 1 — Windows one-click / 方式一 · Windows 一键启动

The lowest-friction path, good for "just get it running on my own PC":

```bat
启动.bat
```

Flow: build & launch the **admin GUI** (if not built) → the GUI auto-starts the backend → prints the LAN address.
- Closing the window = **minimize to the system tray** and keep the service running (tray right-click to open / exit).
- The frontend is already compiled into the backend `wwwroot`; **no separate Node process needed**.
- Other LAN devices reach it at `http://<this-IP>:<port>` in a browser.

Stop:

```bat
停止.bat
```

> If you already have the [Release package](https://github.com/Damianjiang/damian-chat/releases): extract and double-click `WeChatWeb\WeChatWeb.exe` to run (frontend bundled), and `WxAdminGui\WxAdminGui.exe` is the admin GUI.

## 4. Option 2 — Run the .NET backend directly / 方式二 · 直接跑 .NET 后端

If you already have the .NET 8 SDK:

```bat
cd server\WeChatWeb
dotnet run
```

The frontend output ships ready in `wwwroot`. To rebuild the frontend from source:

```bat
cd web
npm install
npm run build-only   # emits to server\WeChatWeb\wwwroot
```

Custom listen address:

```bat
dotnet run --urls http://0.0.0.0:5000
```

## 5. Option 3 — Docker container / 方式三 · Docker 容器

Best for Linux / servers / team sharing:

```bash
docker build -t damian-chat .
docker run -d --name damian-chat \
  -p 8080:8080 \
  -v damian-chat-data:/app/data \
  -v damian-chat-logs:/app/logs \
  damian-chat
# → http://localhost:8080
```

- User data / keys / chat files live in the `damian-chat-data` volume, logs in `damian-chat-logs`; **survive restarts**.
- Change port: `-p 9090:8080` or set `ASPNETCORE_URLS` inside the container.
- Enable HTTPS: add `--env WX_HTTPS_CERT=/app/data/certs/selfsigned.pfx` etc. (next section).

## 6. Enable HTTPS / 启用 HTTPS 加密传输

Default is plain HTTP (fine for an internal LAN). For external / cross-network deployment, **strongly recommend encryption** or anyone can sniff credentials, JWTs, and full chat content:

1. Prepare a PFX certificate (self-signed or a real CA), placed at `data/certs/selfsigned.pfx`.
2. Start with these env vars:
   - `WX_HTTPS_CERT` = PFX path
   - `WX_HTTPS_CERT_PASSWORD` = PFX password
   - `WX_HTTPS_PORT` = listen port(s), comma-separated (e.g. `30011,443`)
3. After restart, the service serves HTTPS on the given ports and 307-redirects plain requests to HTTPS + HSTS.

> The admin GUI talks to the backend on the **local loopback plain port**; after enabling TLS it uses the `main-port+1` loopback plain port, never exposed to the outside.

## 7. First account & basic use / 首个账号 & 基本使用

The repo ships "clean" — no pre-created accounts:

- **Admin GUI**: "User management" → **Create user** (account + nickname + password; leave password empty to get a random one shown once). This first account is what you log in with.
- **Or console / direct registration**: open registration requires the **registration key** (admin configures in "Security settings"); switch to email registration by configuring SMTP.

After logging in:
- Add friends / start a group (shortcuts on the contacts tab)
- Send / recall / quote / pat-a-pat
- Post to Moments
- In Settings, change theme / dark mode / font size / UI language

## 8. Day-to-day ops (admin GUI) / 日常运维（管理端 GUI）

Double-click `WxAdminGui` (or run `启动.bat`). Tab-by-tab:

| Tab | What it does |
|---|---|
| Overview | start / stop / restart service, change port, view online users (3 s auto-refresh), start-with-OS |
| Security settings | registration key / registration mode / dual-captcha toggles / login lockout / rate limits / firewall params / SMTP / 大面号 cooldown |
| User management | create / delete users, ban / unban (instant disconnect), reset password (instant token revocation) |
| User feedback | view / copy / delete items from the client "Settings → Issue feedback" |
| Honeypot | on/off + tarpit, view attack events and canaries (which decoy got stolen by whom) |
| Slider captcha images / login background | upload replacement backgrounds for the slider test and login screen (ripple / solid / image) |
| Logs / console | live view of `requests` / `auth` / `reports` / console output |

> Closing the window just moves it to the tray; the service keeps running. "Exit software" from the tray is what actually stops it.

## 9. Troubleshooting / 常见问题排查

| Symptom | Cause / fix |
|---|---|
| Can't open the page / connection refused | Service not running or wrong port: admin GUI "Overview" shows "Running", verify the port; for external access confirm it listens on `0.0.0.0`, not `127.0.0.1` |
| LAN devices can't reach it | Open the port in the local firewall; listen address must include `0.0.0.0`; check the IP |
| Tray background unreachable after a while | Host / VM idles the machine → the admin GUI now blocks system sleep in tray state; or set the server to "never sleep" |
| Changed port but no effect | Port is a startup argument; you must **restart the service** |
| Admin GUI can't read data | Service not running; or admin GUI and backend aren't on the same machine (admin API is loopback-only) |
| Images / files won't download | File has been cleaned up (retention days in "Security settings"), or ownership check failed (you can only download media you uploaded or received) |
| Large file upload fails | Default per-file cap is 1 GB (adjustable in "Security settings → File limits"); check disk space |
| Forgot password | Admin GUI "User management → Reset password" (instantly revokes all of that user's login sessions) |

## 10. Configuration reference / 配置参考

| File / variable | Purpose |
|---|---|
| `server/WxAdmin/wxadmin.json` | Admin GUI config: port, start-with-OS, auto-start service |
| `server/WeChatWeb/security.json` | Security policy: registration key / mode, captcha, rate limits, firewall, SMTP, 大面号 cooldown (admin GUI saves apply immediately) |
| `server/WeChatWeb/data/` | Runtime data: `store.json` (users / chats), `files/` (media), `jwt.secret` / `admin.key` (locally generated keys), `feedback.jsonl` |
| `WX_HTTPS_CERT` / `WX_HTTPS_CERT_PASSWORD` / `WX_HTTPS_PORT` | Enable HTTPS (see §6) |
| `ASPNETCORE_URLS` | Override listen address |
| `.env` | Optional overrides for `appsettings` (e.g. Bing API Key); **no real secrets here** — they only exist on the local `data/` |

**Logs**: `logs/requests.jsonl` (request audit — high-frequency polling & file streams are skipped), `logs/auth.jsonl` (login / admin ops), `logs/reports.jsonl` (reports). Each file has a size cap + a 7-day periodic cleanup, so disk usage stays bounded.

---

# 中文

## 1. 它能做什么

- **类微信聊天**：实时私聊 / 多人群聊、消息撤回 / 引用 / 转存 / 删除、拍一拍、@成员 / @全体、群公告 + 确认回执。
- **QQ 式群管理**：按群号加群（开放 / 答题 / 审批 / 关闭四档）、全员禁言 / 按人限时禁言、群投票。
- **朋友圈**：图文发布、点赞、评论。
- **富媒体**：图片 / 语音 / 文件；多端 IndexedDB 缓存 + 离线兜底。
- **账号体系**：邮箱验证码注册（SMTP）或账号直接注册；微信式「大面号」（随机号默认不可搜，防批量加人）。
- **深度安全加固（内置零配置）**：流量防火墙 + 自动封禁、双验证码、httpOnly 刷新令牌、隔离式 Web 蜜罐、管理接口环回 + 密钥双门、敏感词过滤。
- **图形化管理端**（Windows WPF 托盘）：启停服务、改端口、看在线用户、改安全策略、管用户、看日志 / 反馈。

数据 100% 落在你自己机器，无云账号、无遥测。

## 2. 部署前准备

| 需要 | 说明 |
|---|---|
| 操作系统 | 后端跨平台；**图形管理端仅 Windows**（WPF）。Linux / macOS 用 Docker 或直接跑后端 |
| .NET SDK 8 | `dotnet --version` 确认。管理端 / 后端编译依赖 |
| 浏览器 | Chrome / Edge / Safari / 现代 Firefox 均可 |
| 端口 | 默认 `5000`，可改（管理端 / `--urls` / `ASPNETCORE_URLS`） |

> 首次启动会在 `server/WeChatWeb/data/` 生成密钥与数据目录（已被 `.gitignore` 排除，不进仓库、不上传）。

## 3. 方式一 · Windows 一键启动（推荐新手）

最省事的方式，适合「在自己电脑上先跑起来」：

```bat
启动.bat
```

流程：编译并拉起**图形管理端**（若未编译）→ 管理端自动启动后端 → 打印局域网访问地址。
- 关闭窗口 = **最小化到系统托盘**后台常驻（托盘右键可打开 / 退出）。
- 前端已编译进后端 `wwwroot`，**无需单独启动 Node**。
- 局域网内其他设备用浏览器访问 `http://<本机IP>:<端口>` 即可。

停止：

```bat
停止.bat
```

> 已拿到 [Release 打包产物](https://github.com/Damianjiang/damian-chat/releases) 的用户：解压后双击 `WeChatWeb\WeChatWeb.exe` 即运行（自带前端），`WxAdminGui\WxAdminGui.exe` 是图形管理端。

## 4. 方式二 · 直接跑 .NET 后端

已有 .NET 8 SDK 时：

```bat
cd server\WeChatWeb
dotnet run
```

前端产物默认已随仓库就绪（`wwwroot`）。如需从源码重新构建前端：

```bat
cd web
npm install
npm run build-only   # 输出到 server\WeChatWeb\wwwroot
```

自定义监听：

```bat
dotnet run --urls http://0.0.0.0:5000
```

## 5. 方式三 · Docker 容器

适合 Linux / 服务器 / 团队共享：

```bash
docker build -t damian-chat .
docker run -d --name damian-chat \
  -p 8080:8080 \
  -v damian-chat-data:/app/data \
  -v damian-chat-logs:/app/logs \
  damian-chat
# → http://localhost:8080
```

- 用户数据 / 密钥 / 聊天文件落在 `damian-chat-data` 卷，日志在 `damian-chat-logs` 卷，**重启不丢**。
- 改端口：`-p 9090:8080` 或容器内 `ASPNETCORE_URLS`。
- 上 HTTPS：加 `--env WX_HTTPS_CERT=/app/data/certs/selfsigned.pfx` 等（见下节）。

## 6. 启用 HTTPS 加密传输

默认是明文 HTTP（内网自托管够用）。对外 / 跨网络部署**强烈建议加密**，否则登录凭据、JWT、聊天全文可被抓包：

1. 准备一个 PFX 证书（自签或正式证书），放到 `data/certs/selfsigned.pfx`。
2. 设置环境变量启动：
   - `WX_HTTPS_CERT` = PFX 路径
   - `WX_HTTPS_CERT_PASSWORD` = PFX 密码
   - `WX_HTTPS_PORT` = 监听端口，支持逗号分隔多个（如 `30011,443`）
3. 重启服务后，服务在指定端口提供 HTTPS，并对外部明文请求 307 跳转 HTTPS + HSTS。

> 图形管理端会读取「本机环回明文口」与后端通信；启用 TLS 后该管理口默认走 `主端口+1` 的环回明文口，不对外暴露。

## 7. 首个账号 & 基本使用

仓库是「干净项目」，没有预置账号：

- **图形管理端**：「用户管理」页 → **创建用户**（账号 + 昵称 + 密码；密码留空则随机生成并只显示一次）。这是第一个账号，用它登录。
- **或控制台 / 直接注册**：开放注册时需填**注册密钥**（管理员在「安全设置」配置）；改为邮箱注册则配置 SMTP。

登录后：
- 添加好友 / 发起群聊（通讯录快捷入口）
- 收发消息、撤回、引用、拍一拍
- 发布朋友圈
- 设置里可改主题色 / 暗色 / 字号 / 界面语言

## 8. 日常运维（管理端 GUI）

双击 `WxAdminGui`（或 `启动.bat`），各页签功能：

| 页签 | 能做什么 |
|---|---|
| 概览 | 启停 / 重启服务、改端口、看在线用户（3 s 自动刷新）、开机自启 |
| 安全设置 | 注册密钥 / 注册方式 / 双验证码开关 / 登录锁定 / 限流阈值 / 防火墙参数 / SMTP / 大面号冷却 |
| 用户管理 | 创建 / 删除用户、封禁 / 解封（即时踢线）、重置密码（即时吊销令牌） |
| 用户反馈 | 查看 / 复制 / 删除客户端「设置 → 问题反馈」提交 |
| 蜜罐 | 开关 + tarpit、查看攻击事件与金丝雀（被谁套走了哪种诱饵） |
| 滑块图库 / 登录背景 | 上传替换滑块验证背景、登录页背景（波纹 / 纯色 / 图片） |
| 日志 / 控制台 | 实时查看 `requests` / `auth` / `reports` / 控制台输出 |

> 关闭窗口只是进托盘，服务仍后台运行；托盘「退出软件」才真正结束。

## 9. 常见问题排查

| 现象 | 原因 / 解决 |
|---|---|
| 打不开页面 / 连接被拒 | 服务没起或端口不对：管理端「概览」确认「运行中」，核对端口；外部访问确认监听的是 `0.0.0.0` 而非 `127.0.0.1` |
| 局域网设备访问不了 | 本机防火墙放行端口；监听地址要含 `0.0.0.0`；IP 是否填对 |
| 托盘后台过一会儿访问不了 | 宿主 / 虚拟机把空闲机器睡了 → 管理端现在托盘态会阻止系统休眠；或服务器设「永不睡眠」 |
| 改了端口没生效 | 端口是启动参数，需**重启服务** |
| 管理端读不到数据 | 服务未运行；或管理端与后端不在同一台机器（管理 API 仅环回） |
| 图片 / 文件下不来 | 该文件已过期被清理（保留天数见「安全设置」），或归属校验不通过（只能下载自己 / 收到的媒体） |
| 上传大文件失败 | 单文件上限默认 1 GB（可在「安全设置 → 文件限制」调）；检查磁盘空间 |
| 忘记密码 | 管理端「用户管理 → 重置密码」（会即时吊销其全部登录态） |

## 10. 配置参考

| 文件 / 变量 | 作用 |
|---|---|
| `server/WxAdmin/wxadmin.json` | 管理端配置：端口、开机自启、启动时自动起服务 |
| `server/WeChatWeb/security.json` | 安全策略：注册密钥 / 方式、验证码、限流、防火墙、SMTP、大面号冷却（管理端保存即时生效） |
| `server/WeChatWeb/data/` | 运行时数据：`store.json`（用户 / 聊天）、`files/`（媒体）、`jwt.secret` / `admin.key`（本地生成密钥）、`feedback.jsonl` |
| `WX_HTTPS_CERT` / `WX_HTTPS_CERT_PASSWORD` / `WX_HTTPS_PORT` | 启用 HTTPS（见第 6 节） |
| `ASPNETCORE_URLS` | 覆盖监听地址 |
| `.env` | 可选覆盖 `appsettings`（如 Bing API Key）；**真实密钥不在这里**，只在本机 `data/` 首启生成 |

**日志**：`logs/requests.jsonl`（请求审计，已跳过高频轮询与文件流）、`logs/auth.jsonl`（登录 / 管理操作）、`logs/reports.jsonl`（举报）。单文件有容量上限 + 7 天定期清理，磁盘占用有界。
