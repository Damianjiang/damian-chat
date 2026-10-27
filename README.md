# 大面聊天 · Damian Chat

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](./LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![Vue](https://img.shields.io/badge/Vue-3.5-42b883?logo=vue.js)](https://vuejs.org)
[![TypeScript](https://img.shields.io/badge/TypeScript-5.9-blue)](https://www.typescriptlang.org)
[![Docker](https://img.shields.io/badge/Docker-ready-2496ED?logo=docker)](https://docs.docker.com)
[![Issues](https://img.shields.io/badge/issues-welcome-green)](https://github.com/Damianjiang/damian-chat/issues)

A **self-hosted, WeChat-style instant messaging platform** with real-time group & private chat, Moments (朋友圈), rich media, QQ-style join-by-group-number, and a hardened security layer — **C# ASP.NET Core 8 + SignalR** on the backend, **Vue 3 + Vuetify 4 + TypeScript** on the frontend. No third-party SaaS; it just runs on your machine.

一个 **可自托管、类微信的即时通讯系统**：实时群聊 / 私聊、朋友圈、富媒体、QQ 式按群号加群，并内置一层**安全加固**——后端 **C# ASP.NET Core 8 + SignalR**，前端 **Vue 3 + Vuetify 4 + TypeScript**，开箱即用，无需依赖任何第三方 SaaS。

> Fully open source · self-hosted · your data never leaves your machine. / 完全开源 · 本地部署 · 数据只在你自己的机器上。

---

## 界面展示 · Screenshots

<p align="center">
  <img src="docs/chat.png" width="440" alt="聊天列表 Chat list">
  <img src="docs/group.png" width="440" alt="群聊设置 Group chat & settings">
</p>

<p align="center">
  <img src="docs/private.png" width="440" alt="私聊 Private chat">
  <img src="docs/moments.png" width="440" alt="朋友圈 Moments">
</p>

<p align="center">
  <img src="docs/contacts.png" width="440" alt="通讯录 Contacts">
  <img src="docs/dark.png" width="440" alt="暗色模式 Dark mode">
</p>

> 群设置页里的「加群申请 / 入群方式 · 需群主审批」对应后端 QQ 式加群机制（开放群直接进 / 审批群等群主通过，群号可一键复制）；暗色模式下自己发的消息气泡跟随主题，亮/暗都清晰。
> The "join request / join policy (owner approval)" on the group-settings page is the QQ-style join flow; in dark mode your own bubbles follow the theme and stay readable in both light & dark.

---

## 项目亮点 · Highlights

- 🔐 **数据自持 / Data ownership** — 用户、聊天、文件全部保存在你自己的目录，没有云账号、没有遥测。All users, chats and files live in your own directory; no cloud account, no telemetry.
- ⚡ **实时通讯 / Realtime** — 基于 SignalR 的 WebSocket 实时推送：新消息、撤回、入群、成员变动即时同步。WebSocket realtime push via SignalR: new messages, recall, joins and member changes sync instantly.
- 🛡️ **深度安全加固 / Security hardening** — 流量防火墙（按 IP 并发+速率+自动封禁）、身份维度消息洪泛上限、refresh 走 httpOnly cookie、滑块+图形双验证码、隔离式 Web 蜜罐、群/消息/会话全入口成员校验、敏感词过滤、管理接口环回+密钥双门。 A traffic firewall (per-IP concurrency + rate + auto-ban), per-identity message-flood cap, httpOnly refresh tokens, dual slider + image captcha, an isolated web honeypot, per-entrypoint group/message/conversation authorization, sensitive-word filtering, and a loopback + key gated admin API.
- 🎨 **贴近微信的交互 / WeChat-like UX** — 会话列表、群成员宫格、拍一拍、引用回复、@成员、@全体成员、暗色模式、字号个性化、离线 Service Worker 缓存。 Conversation list, group-member grid, "pat a pat", quote reply, @member / @everyone, dark mode, font-size personalization, offline Service Worker cache.
- 🖥️ **图形化管理端 / Admin GUI** — WPF 托盘常驻管理工具：一键启停服务、改端口、看在线用户、改安全策略、管用户、看日志/反馈。 A WPF tray-resident admin tool: start/stop the service, change the port, view online users, tweak security policy, manage users, and read logs / feedback.

---

## 功能总览 · Features

### 聊天 · Messaging
- 私聊 1v1、多人群聊；文本、图片（等比缩放 + 点击预览）、语音、文件。1v1 private + multi-person group chats; text, images (aspect-fit + click-to-preview), voice, files.
- 消息**撤回 / 引用回复 / 转存 / 删除**。Recall / quote-reply / save / delete messages.
- 群聊**拍一拍 / @成员 / @全体成员 / 群公告 + 确认回执**。Group "pat a pat", @member, @everyone, group announcement + confirmation receipt.
- **QQ 式按群号加群**：开放群直接进 / 审批群等群主通过，群号可一键复制。QQ-style join-by-group-number: open groups join directly, approval groups wait for the owner; group numbers are one-click copyable.
- 会话**置顶 / 免打扰 / 折叠**，多端 IndexedDB 缓存 + 离线兜底。Pin / mute / collapse conversations; multi-device IndexedDB cache + offline fallback.

### 个人与社交 · Profile & Social
- 注册 / 登录（双验证码 + 强度策略 + 失败锁定）。Register / login with dual captcha + strength policy + failure lockout.
- 头像、昵称、个性签名、性别、地区、手机号隐私开关。Avatar, nickname, signature, gender, region, and phone-privacy toggle.
- 通讯录 + 好友关系，成员资料卡。Contacts + friend relationships and member profile cards.
- **朋友圈**：发布图文 / 媒体，点赞与评论。Moments: post text/media, like and comment.
- 个性化：主题色、暗色、半透明、背景、字号（小/标准/大/特大，跨刷新保留）。Personalization: theme color, dark mode, translucency, wallpaper, font size (S/M/L/XL, persists across refresh).

### 管理 · Admin
- 在线用户实时查看、封禁 / 解封。Real-time online users, ban / unban.
- 用户与群的管理、公告 / 确认。User and group management, announcements / confirmations.
- 安全策略热改（限流、封禁阈值、蜜罐开关）。Hot-tweak security policy (rate limits, ban thresholds, honeypot switch).
- 反馈收集 + 删除、日志 / 控制台查看。Feedback collection + deletion, log / console viewing.

---

## 技术栈 · Tech stack

| 层 · Layer | 技术 · Tech |
|---|---|
| 后端 · Backend | C# / .NET 8 · ASP.NET Core · SignalR · JWT · custom middleware |
| 前端 · Frontend | Vue 3 · Vuetify 4 · Pinia · TypeScript · Vite |
| 实时 · Realtime | @microsoft/signalr (WebSocket) |
| 媒体 · Media | IndexedDB blob cache (client) · server file store |
| 管理端 GUI · Admin GUI | WPF (.NET 8 Windows) · tray-resident |
| 构建 · Build | .NET SDK · Vite 7 · vue-tsc |

> 前端产物由 Vite 直接输出到后端 `wwwroot`，**部署/运行只需要 .NET 后端，无需 Node 常驻**。
> The frontend bundle is emitted straight into the backend `wwwroot` — **at runtime you only need the .NET backend; no Node process required**.

---

## 目录结构 · Repository layout

```
.
├── server/                  后端 .NET 解决方案 / backend .NET solution
│   ├── WeChatWeb/           ASP.NET Core 主服务 / main service (controllers / hubs / middleware / services)
│   │   ├── Controllers/     REST API (auth / users / conversations / groups / moments / files / admin …)
│   │   ├── Hubs/            SignalR ChatHub (realtime push / online-connection management)
│   │   ├── Middleware/      firewall / rate limit / honeypot / logging / security headers …
│   │   ├── Services/        JWT / keys / captcha / sensitive words / honeypot / feedback / watchdog …
│   │   ├── Data/            数据层（DataStore / 消息洪泛限流器，随库持久化到 data/store.json）
│   │   └── Models/          实体 / entities (User / Group / Message / Moment …)
│   ├── WxAdminGui/          WPF 图形管理端（托盘常驻）/ WPF GUI admin (tray-resident)
│   ├── WxAdmin/             命令行管理工具 / CLI admin tool
│   └── Shared/              跨工程共享（管理密钥读取）/ shared (admin-key reader)
├── web/                     前端 Vue 3 工程 / frontend Vue 3 project
│   ├── src/
│   │   ├── views/           登录页 + 主框架内各面板 / login + main-shell panes
│   │   ├── components/      消息气泡 / 输入工具条 / 滑块验证码 / 头像 …
│   │   ├── stores/          Pinia：auth / conversations / contacts / moments / theme
│   │   ├── lib/             api / signalr / idb / files / theme / personalize …
│   │   └── styles/          微信风 SCSS + Vuetify 主题
│   └── public/              Service Worker / 启动脚本 / PWA 清单
├── docs/                    README 截图 / README screenshots
├── 启动.bat                 一键启动（拉起管理端 → 启动服务）/ one-click start
└── 停止.bat                 一键停止（按配置端口清理监听）/ one-click stop
```

---

## 快速开始 · Quick start

**前置条件 · Prerequisites**：Windows（管理端 GUI 为 WPF；核心后端跨平台，GUI 仅 Windows）；.NET 8 SDK（`dotnet --version` 确认）；浏览器访问 `http://<局域网IP>:<端口>`。
Windows (admin GUI is WPF; the backend core is cross-platform, only the GUI is Windows-only); .NET 8 SDK; open `http://<lan-ip>:<port>` in a browser.

**方式一 · 一键启动 / Option 1 · One-click (recommended)**：
```bat
启动.bat
```
编译并拉起图形管理端（若未编译）→ 管理端自动启动后端服务 → 打印局域网访问地址；关闭窗口 = 最小化到托盘后台常驻。
It compiles & launches the GUI admin (if not built) → the admin auto-starts the backend → prints the LAN address. Closing the window just minimizes it to the tray.

**方式二 · 直接跑后端 / Option 2 · Run backend directly**：
```bat
cd server\WeChatWeb
dotnet run
```

**前端构建（可选，默认已随仓库就绪）/ Build frontend (optional, bundled by default)**：
```bat
cd web
npm install
npm run build-only     # 产物输出到 server\WeChatWeb\wwwroot / emits to server\WeChatWeb\wwwroot
```

**停止 / Stop**：`停止.bat`

**方式三 · Docker 容器（Linux / 服务器 / 跨平台）/ Option 3 · Docker**：
```bash
docker build -t damian-chat .
docker run -d --name damian-chat \
  -p 8080:8080 \
  -v damian-chat-data:/app/data \
  -v damian-chat-logs:/app/logs \
  damian-chat
# → http://localhost:8080
```
数据 / 密钥 / 聊天文件都落在 `data` 卷、日志在 `logs` 卷，重启不丢；端口可用 `ASPNETCORE_URLS` 覆盖；上 HTTPS 设 `WX_HTTPS_CERT`（PFX）+ `WX_HTTPS_CERT_PASSWORD` + `WX_HTTPS_PORT`。
Data/keys/files live in the `data` volume, logs in the `logs` volume (survive restarts). Override the port with `ASPNETCORE_URLS`; enable HTTPS with `WX_HTTPS_CERT`.

> 端口以管理端配置为准；首次运行会在 `server/WeChatWeb/data/` 下生成密钥与数据目录（已被 `.gitignore` 排除，不在仓库中）。
> The port is managed by the admin tool; on first run a `data/` folder with generated keys is created (excluded from the repo).

---

## 安全说明 · Security notes

本系统把「聊天数据留在你自己的机器上」作为前提，再叠加一整套面向攻击场景的加固。以下都是**产品内置、无需额外配置**的安全能力。
This system treats "your chat data stays on your own machine" as a premise, then layers a hardening stack aimed at real attack scenarios. Everything below is **built-in and needs no extra configuration**.

- **数据不出本机 / Data never leaves your machine** — 用户、聊天、文件全部落盘在你的 `data/`，没有云账号、没有遥测、没有第三方回调。
- **密钥首次启动本地生成 / Keys generated locally on first run** — JWT 签名密钥与管理密钥随机生成并写在本地，**源码与仓库里不含任何可用密钥**。
- **登录防爆破 / Login anti-brute-force** — 滑块 + 图形双验证码、失败锁定、背景图库可替换；密码 PBKDF2 加盐哈希 + 强度策略。
- **限流与 DDoS 防护 / Rate limiting & DDoS defense** — 按 IP 并发/速率限制 + 自动封禁，叠加**身份维度**消息洪泛上限（防"偷 token 换 IP"绕过 IP 限流）。
- **长效凭证走 httpOnly Cookie / Long-lived creds in httpOnly cookie** — refresh token 存 httpOnly cookie（防 XSS 直接窃取），访问 token 仅 2h。
- **全入口成员鉴权 / Per-entrypoint authorization** — 群号是不可枚举 GUID，非成员读/写一律 404/403；拉人只能拉自己的好友、改角色仅群主、审批仅群主/管理员。
- **敏感词 + 管理接口环回双门 / Sensitive-word filter + loopback-gated admin API** — 消息入口剥离敏感词；`/api/admin/*` 仅接受环回且需本地管理密钥。
- **隔离式 Web 蜜罐 / Isolated web honeypot** — 假 `.env`/`.git`/管理台 + 金丝雀 + tarpit 拖延，只读、与真实系统完全隔离，对正常用户零误伤。
- **封号/删号即时踢线 / Instant ban/delete disconnect** — 已认证请求按 sub 反查封禁状态，命中即 `Abort` 在线连接。

> **生产部署建议 / Production tips**：置于 HTTPS 之后（`WX_HTTPS_CERT`）、管理接口限制环回、按网络环境调整 `security.json` 的限流阈值与封禁线。

---

## License

以 **MIT License** 开源 / Released under the **MIT License**, see [`LICENSE`](LICENSE).

---

## 目录 · Contents

- [界面展示 · Screenshots](#界面展示--screenshots)
- [项目亮点 · Highlights](#项目亮点--highlights)
- [功能总览 · Features](#功能总览--features)
- [技术栈 · Tech stack](#技术栈--tech-stack)
- [目录结构 · Repository layout](#目录结构--repository-layout)
- [快速开始 · Quick start](#快速开始--quick-start)
- [安全说明 · Security notes](#安全说明--security-notes)
- [License](#license)
