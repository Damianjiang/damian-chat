# Damian Chat · 大面聊天

A **self-hosted, WeChat-style instant messenger** that runs entirely on your own machine —
realtime private / group chat, Moments, QQ-style group management, email-code registration
and a WeChat-like "大面号" custom ID system, plus a **built-in, zero-config security hardening layer**.

Backend: **C# / ASP.NET Core 8 + SignalR** · Frontend: **Vue 3 · Vuetify 4 · TypeScript**.
100% data ownership · no cloud account · no telemetry · no third-party SaaS · ships ready to run.

> 可自托管、类微信（WeChat-style）的开源即时通讯系统：实时私聊 / 群聊、朋友圈、QQ 式群管理、
> 邮箱注册与微信式「大面号」，并内置一层**开箱即用的深度安全加固**。数据 100% 自持，无云账号、无遥测。

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](./LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![Vue](https://img.shields.io/badge/Vue-3.5-42b883?logo=vue.js)](https://vuejs.org)
[![TypeScript](https://img.shields.io/badge/TypeScript-5.9-blue)](https://www.typescriptlang.org)
[![Docker](https://img.shields.io/badge/Docker-ready-2496ED?logo=docker)](https://docs.docker.com)

> **Docs / 文档**
> - Deployment & usage (bilingual) / 部署与使用（中英双语）→ [docs/deployment-guide.md](docs/deployment-guide.md)
> - Windows x64 release package / 一键运行包 → [Releases](https://github.com/Damianjiang/damian-chat/releases)
> - 下方「# 中文」一节是给中文读者准备的同一份内容。

All screenshots below are **real running UI** captured from a clean demo instance
(demo accounts + one group + moments + mixed messages — **no real user data**; demo accounts deleted after capture).

---

## Features / 功能

Every screen below has a real screenshot, English first. 下方每个界面都配了实跑截图。

**Chat list / 聊天列表** — navigation rail (Chat · Contacts · Moments · Discover · Me), foldable group section, unread badges, pinning.

<p align="center">
  <img src="docs/images/chat-list.png" width="720" alt="Chat list" style="border-radius:8px">
</p>

**Private chat / 私聊** — text, **quote reply**, **image** (aspect-fit + preview), **voice** clip, emoji.

<p align="center">
  <img src="docs/images/private-chat.png" width="720" alt="Private chat" style="border-radius:8px">
</p>

**Group chat stream / 群聊消息流** — system messages (group created / pat-a-pat), text, **@member**, **group poll card**, images, files.

<p align="center">
  <img src="docs/images/group-messages.png" width="720" alt="Group chat stream" style="border-radius:8px">
</p>

**Group settings / 群聊设置** — member grid (owner badge, invite / remove), group avatar & name, **announcement**, in-group nickname, **mute / pin / search / mute-all** toggles.

<p align="center">
  <img src="docs/images/group-settings.png" width="720" alt="Group settings" style="border-radius:8px">
</p>

**Moments / 朋友圈** — post text / media, **like**, **comment**, card-style feed.

<p align="center">
  <img src="docs/images/moments.png" width="720" alt="Moments" style="border-radius:8px">
</p>

**Contacts / 通讯录** — friend list + member profile cards (avatar / nickname / 大面号 / gender / region / signature).

<p align="center">
  <img src="docs/images/contacts.png" width="720" alt="Contacts" style="border-radius:8px">
</p>

**Discover / 发现** — join-by-group-number (open / question / approve / closed), find friends, group discovery.

<p align="center">
  <img src="docs/images/discovery.png" width="720" alt="Discover" style="border-radius:8px">
</p>

**Global search / 全局搜索** — full-text across **messages / contacts / groups / moments**, results grouped by type.

<p align="center">
  <img src="docs/images/search.png" width="720" alt="Global search" style="border-radius:8px">
</p>

**My profile / 我的资料** — avatar / nickname / 大面号 / email / signature / region, editable fields + privacy toggles.

<p align="center">
  <img src="docs/images/me.png" width="720" alt="My profile" style="border-radius:8px">
</p>

**Settings / 设置** — theme presets, dark / follow-system, UI language, **font size (S/M/L/XL)**, translucency / wallpaper, privacy toggles, **issue feedback**, About (version + build fingerprint).

<p align="center">
  <img src="docs/images/settings.png" width="720" alt="Settings" style="border-radius:8px">
</p>

**Dark mode / 暗色模式** — same palette auto-adapts to dark; switching is instant and persists across refresh / device change.

<p align="center">
  <img src="docs/images/chat-list-dark.png" width="720" alt="Dark mode" style="border-radius:8px">
</p>

**Mobile layout / 手机布局** — the same code auto-switches to a mobile layout (bottom tab bar + single-column stream) at ~390 px.

<p align="center">
  <img src="docs/images/chat-list-mobile.png" width="320" alt="Mobile layout" style="border-radius:8px">
</p>

### Feature list / 功能明细

**Messaging / 聊天**
- 1v1 private + multi-person group chats; text, images (aspect-fit + preview), voice, files;
- **recall / quote-reply / save / delete** messages;
- **pat-a-pat in both private and group chats** — double-tap the other avatar, or use the explicit "Pat" menu item (no click-vs-double-click ambiguity); @member / @everyone, group announcement + confirmation receipt;
- **QQ-style group join & management**: join by group number with four policies (open / answer-question / apply-with-reason / closed); **mute-all + per-member timed mute** (10 min ~ permanent); owner / admin tiered permissions;
- **Group polls**: single/multi choice, live vote bars, closable;
- pin / mute / collapse conversations; multi-device IndexedDB cache + offline fallback.

**Profile & social / 个人与社交**
- register / login with dual captcha + strength policy + failure lockout; **email-code registration** (SMTP configurable in the admin GUI) or direct account registration;
- **大面号 (custom ID) system**: random unsearchable default (mirrors WeChat `wxid`, anti mass-add), changeable with a configurable cooldown; bind / re-bind email with verification codes;
- **Search privacy ("ways to add me")**: independent toggles for being found by phone or by custom ID;
- avatar / nickname / signature / gender / region / phone-privacy toggles;
- **Moments**: post text / media, like, comment;
- personalization: theme color, dark mode, translucency, wallpaper, font size (S/M/L/XL, persists across refresh).

**Admin GUI / 图形化管理端 (Windows tray)**
- real-time online users, ban / unban (instant disconnect), create / delete users (cascading cleanup), reset password (instant token revocation);
- user & group management, announcements / confirmations;
- hot-tweak security policy (rate limits, ban thresholds, honeypot switch, registration mode / key / SMTP, ID-change cooldown) with a one-click SMTP test email;
- feedback collection + deletion, log / console viewing.

### Why this over the alternatives / 为什么选它

| Need | Damian Chat | DIY IM (Matrix etc.) | General group chat (Rocket.Chat / Mattermost) | AI chat (GPT clone) |
|---|---|---|---|---|
| WeChat-like UX (Moments / pat / custom ID / quote / recall) | ✅ native | ❌ | ❌ | ❌ |
| Realtime private + group chat (WebSocket) | ✅ SignalR | ✅ | ✅ | — |
| **Full data ownership** (no cloud / SaaS / telemetry) | ✅ local only | ⚠️ | ⚠️ often cloud | ⚠️ often cloud |
| **Built-in security hardening** | ✅ | ❌ build it yourself | ⚠️ basic | ❌ |
| **One-file / one-line deploy** | ✅ | ⚠️ | ⚠️ multi-service | ⚠️ |
| GUI admin tool (Windows tray) | ✅ | ❌ | ⚠️ web | ❌ |
| QQ-style join + mute-all / timed mute + group polls | ✅ | ❌ | ⚠️ partial | ❌ |
| Bilingual (EN / 中文) + personalization | ✅ | ❌ | ⚠️ | ❌ |

**Right for you if / 适合场景**: you want "your own WeChat" on a LAN / private server — realtime, secure, data that never leaves your machine, polished and usable — without assembling an IM protocol from scratch or paying for a SaaS subscription.

### Quick start / 快速开始

**Prerequisites / 前置条件**: Windows (the admin GUI is WPF; the backend core is cross-platform, only the GUI is Windows-only) · **.NET 8 SDK** (`dotnet --version`) · a browser.

**Option 1 · One-click / 一键启动 (recommended):**
```bat
启动.bat
```
Builds & launches the GUI admin (if not built) → admin auto-starts the backend → prints the LAN address. Closing the window just minimizes it to the tray.

**Option 2 · Run the backend directly / 直接跑后端:**
```bat
cd server\WeChatWeb
dotnet run
```

**Option 3 · Docker (Linux / servers / cross-platform):**
```bash
docker build -t damian-chat .
docker run -d --name damian-chat \
  -p 8080:8080 \
  -v damian-chat-data:/app/data \
  -v damian-chat-logs:/app/logs \
  damian-chat
# → http://localhost:8080
```

Then open `http://<lan-ip>:<port>`. First launch creates `data/` with locally generated keys.
Full deployment, HTTPS, first account, admin-GUI ops, troubleshooting, and the config reference are in **[docs/deployment-guide.md](docs/deployment-guide.md)**.

### Tech stack / 技术栈

| Layer | Tech |
|---|---|
| Backend | C# / .NET 8 · ASP.NET Core · SignalR · JWT · custom middleware |
| Frontend | Vue 3 · Vuetify 4 · Pinia · TypeScript · Vite |
| Realtime | @microsoft/signalr (WebSocket) |
| Media | IndexedDB blob cache (client) · server file store |
| Admin GUI | WPF (.NET 8 Windows) · tray-resident |

> The frontend bundle is emitted straight into the backend `wwwroot` — **at runtime you only need the .NET backend; no Node process required**.

### Security (built-in, zero-config) / 安全（内置，零配置）

- **Data never leaves your machine** — users, chats and files stay on disk in your `data/`; no cloud accounts, no telemetry, no third-party callbacks.
- **Keys generated locally on first run** — JWT and admin keys are randomly generated and stored locally; no usable secret is shipped in source or the repo.
- **Login anti-brute-force** — dual slider + image captcha, lockout on repeated failure, replaceable background pools; PBKDF2 salted hashing + strength policy.
- **Rate limiting & DDoS defense** — per-IP concurrency / rate / bandwidth caps + auto-ban, plus a per-identity message-flood limit so a stolen token + IP rotation can't bypass IP limits.
- **Long-lived creds in httpOnly cookie** — refresh tokens live in httpOnly cookies (XSS-resistant); access tokens expire in 2 hours.
- **Per-entrypoint member authorization** — group IDs are non-enumerable GUIDs; non-members get 404/403 on every read/write; you can only add your own contacts; role changes are owner-only.
- **Sensitive-word filter + loopback-gated admin API** — `/api/admin/*` only accepts loopback requests **and** requires the local admin key.
- **Isolated web honeypot** — decoy `.env` / `.git` / admin console + canaries + tarpit delay; read-only, fully isolated, zero impact on real users.
- **Instant ban/delete disconnect** — authenticated requests re-check ban status by subject; a hit immediately aborts the live connection.

> **Production tips / 生产部署建议**: put it behind HTTPS (`WX_HTTPS_CERT`), keep the admin API loopback-only, and tune the rate/ban thresholds in `security.json` for your network.

### Repository layout / 目录结构

```
.
├── server/                    后端 .NET 解决方案
│   ├── WeChatWeb/             主服务（控制器 / Hubs / 中间件 / 服务）
│   ├── WxAdminGui/            WPF 图形管理端（托盘常驻）
│   ├── WxAdmin/               命令行管理工具
│   └── Shared/                跨工程共享（管理密钥读取）
├── web/                       前端 Vue 3 工程
├── docs/                      文档 + 界面截图（images/）
│   ├── deployment-guide.md    部署与使用指南（中英双语）
│   └── images/                实跑界面截图
├── 启动.bat / 停止.bat         一键启动 / 停止
└── README.md                  本文件（中英双语主文档 / this file）
```

### License / 许可

Released under the [MIT License](./LICENSE).

---

# 中文（简体）

下面是给中文读者的同一份内容。英文在上面，中文整块在这里，不需要来回切换。

## 功能

每个界面都配了**真实运行截图**（来自全新演示实例，不含任何真实用户数据；演示账号截图后已删除）。

- **聊天**：私聊 1v1、多人群聊；文本 / 图片（等比缩放 + 预览）/ 语音 / 文件；**消息撤回 / 引用回复 / 转存 / 删除**；**私聊、群聊都能拍一拍**（双击对方头像，或右键 / 长按菜单里的「拍一拍」，不再和"单击开资料卡"打架）；@成员 / @全体、群公告 + 确认回执；
- **QQ 式群管理**：按群号加群（开放 / 答题 / 审批 / 关闭四档）、全员禁言 / 按人限时禁言（10 分钟~永久）、群投票（单选 / 多选、实时票数、可截止）；会话置顶 / 免打扰 / 折叠；
- **朋友圈**：图文发布、点赞、评论；
- **账号体系**：双验证码 + 强度策略 + 失败锁定；**邮箱验证码注册**（后台配 SMTP）或账号直注；**大面号**（随机号默认不可搜，防批量加人；可改自定义号，冷却天数可配）；「添加我的方式」搜索隐私开关；
- **个性化**：主题色 / 暗色 / 跟随系统、界面语言、**字号（小 / 标准 / 大 / 特大）**、半透明 / 背景、离线 Service Worker 缓存；
- **图形化管理端**（Windows WPF 托盘）：启停服务、改端口、看在线用户、改安全策略、管用户、看日志 / 反馈。

完整逐界面截图见上方的 **Features / 功能** 一节（截图是中英通用的，放在英文区顶部即可）。

## 为什么选它 / 适合场景

想在局域网 / 私有服务器给团队、家庭、社区做一个「**自己的微信**」——要实时、要安全、要数据不出本机、还要好看好用，而**不想从零拼 IM 协议、不想要 SaaS 订阅**。「为什么选它」的对比表、功能明细、技术栈见上方英文区，内容一一对应。

## 快速开始

**前置**：Windows（管理端 GUI 为 WPF；核心后端跨平台，仅 GUI 限定 Windows）· .NET 8 SDK（`dotnet --version`）· 浏览器。

- **一键启动（推荐）**：`启动.bat`（编译并拉起图形管理端 → 管理端自动启动后端 → 打印局域网访问地址；关窗 = 最小化到托盘后台常驻）
- **直接跑后端**：`cd server\WeChatWeb` 然后 `dotnet run`
- **Docker（Linux / 服务器 / 跨平台）**：`docker build -t damian-chat .` 后 `docker run -d -p 8080:8080 -v damian-data:/app/data damian-chat`

完整部署 / HTTPS 加密 / 首个账号 / 管理端运维 / 排障 / 配置参考，都在 **[docs/deployment-guide.md](docs/deployment-guide.md)**（中英双语，中文在该文件下半部「# 中文」一节）。

## 安全说明

全部**内置、零配置**：数据不出本机 · 密钥首启本地生成 · 登录防爆破（双验证码 + 失败锁定 + PBKDF2）· 按 IP 并发 / 速率 / 带宽限流 + 自动封禁、叠加身份维度消息洪泛上限 · 长效凭证走 httpOnly Cookie · 群号不可枚举 + 非成员全入口 404/403 · 敏感词过滤 + 管理接口环回 + 密钥双门 · 隔离式 Web 蜜罐 · 封号删号即时踢线。详见上方英文「Security」一节。

## 许可

以 [MIT 许可证](./LICENSE) 开源。
