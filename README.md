# Damian Chat · 大面聊天

> **A self-hosted, WeChat-style instant messenger that runs entirely on your own machine.**
> 一个可自托管、类微信（WeChat-style）的开源即时通讯系统：实时私聊 / 群聊、朋友圈、QQ 式群管理、邮箱注册与微信式「大面号」，并内置一层**深度安全加固**。
>
> Backend: **C# / ASP.NET Core 8 + SignalR** · Frontend: **Vue 3 + Vuetify 4 + TypeScript**.
> 100% data ownership, no cloud account, no telemetry, no third-party SaaS. Ships ready to run.

**中文 README / 中文版本** → [README.zh.md](./README.zh.md) · **Simplified-Chinese deep doc** → [部署与使用指南](docs/部署与使用指南.md)

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](./LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![Vue](https://img.shields.io/badge/Vue-3.5-42b883?logo=vue.js)](https://vuejs.org)
[![TypeScript](https://img.shields.io/badge/TypeScript-5.9-blue)](https://www.typescriptlang.org)
[![Docker](https://img.shields.io/badge/Docker-ready-2496ED?logo=docker)](https://docs.docker.com)

Every screen below is a **real screenshot** from a clean demo instance (demo accounts + 1 group + moments + mixed messages — no real user data). The full per-feature walkthrough is in **[📸 Feature Gallery](docs/feature-gallery.md)** (EN) / **[📸 功能一览](docs/功能一览.md)** (中文).

---

## Screenshots / 界面展示

**Chat list** — navigation rail (Chat / Contacts / Moments / Discover / Me), foldable group section, unread badges, pinned sessions.

<p align="center">
  <img src="docs/images/chat-list.png" width="760" alt="Chat list / conversation list" style="border-radius:8px">
</p>

**Private chat** — text, **quote reply**, **image** (aspect-fit + preview), **voice** clip, emoji.

<p align="center">
  <img src="docs/images/private-chat.png" width="760" alt="Private chat with quote, image and voice" style="border-radius:8px">
</p>

**Group chat — message stream** — system messages (group created / pat-a-pat), text, **@mention**, **group poll card**, images, files.

<p align="center">
  <img src="docs/images/group-messages.png" width="760" alt="Group chat message stream with @mention and poll" style="border-radius:8px">
</p>

**Group settings** — member grid (owner badge, invite/remove), group avatar, name, **announcement**, in-group nickname, **mute / pin / search / mute-all** toggles, owner-only transfer/dissolve.

<p align="center">
  <img src="docs/images/group-settings.png" width="760" alt="Group settings panel" style="border-radius:8px">
</p>

**Moments** — post text/media, **like** and **comment**, card-style feed.

<p align="center">
  <img src="docs/images/moments.png" width="760" alt="Moments feed with likes and comments" style="border-radius:8px">
</p>

**Contacts** — friend list + member profile cards (avatar / nickname / 大面号 / gender / region / signature).

<p align="center">
  <img src="docs/images/contacts.png" width="760" alt="Contacts and profile cards" style="border-radius:8px">
</p>

**Settings** — theme presets, dark / follow-system, UI language, **font size (S/M/L/XL)**, translucency / wallpaper, privacy toggles, **issue feedback**, About (version + build fingerprint).

<p align="center">
  <img src="docs/images/settings.png" width="760" alt="Settings: theme, font size, privacy, feedback" style="border-radius:8px">
</p>

**Dark mode** — the same theme palette auto-adapts to dark; switching is instant and persists across refreshes / devices.

<p align="center">
  <img src="docs/images/chat-list-dark.png" width="760" alt="Dark mode" style="border-radius:8px">
</p>

**Mobile layout** — the same codebase switches to a mobile layout (bottom tab bar + single-column stream) at ~390 px.

<p align="center">
  <img src="docs/images/chat-list-mobile.png" width="340" alt="Mobile responsive layout" style="border-radius:8px">
</p>

> Not shown above but in the gallery: **Discover** (join-by-group-number / search entry), **Global search**, **My profile**, and a **second-POV group poll card**. See the [Feature Gallery](docs/feature-gallery.md).

---

## Why this over the alternatives / 为什么选它

Among similar projects, **Damian Chat** is positioned as a "WeChat-like messenger that is 100% yours" — private, self-hosted IM.

| Need | Damian Chat | DIY IM (Matrix etc.) | General group chat (Rocket.Chat / Mattermost) | AI chat (GPT clone) |
|---|---|---|---|---|
| WeChat-like UX (Moments / pat-a-pat / custom 大面号 ID / quote / recall) | ✅ native | ❌ | ❌ | ❌ |
| Realtime private + group chat (WebSocket) | ✅ SignalR | ✅ | ✅ | — |
| **Full data ownership** (no cloud / SaaS / telemetry) | ✅ local only | ⚠️ depends | ⚠️ often cloud | ⚠️ often cloud |
| **Built-in security hardening** (firewall + honeypot + dual captcha + loopback admin gate) | ✅ | ❌ build it yourself | ⚠️ basic | ❌ |
| **One-file / one-line deploy** | ✅ | ⚠️ | ⚠️ multi-service | ⚠️ |
| GUI admin tool (Windows tray) | ✅ included | ❌ | ⚠️ web | ❌ |
| QQ-style join + mute-all / timed mute + group polls | ✅ | ❌ | ⚠️ partial | ❌ |
| Bilingual (EN / 中文) + personalization (theme / dark / font / offline cache) | ✅ | ❌ | ⚠️ | ❌ |

**Right for you if** you want "your own WeChat" on a LAN / private server — realtime, secure, data that never leaves your machine, polished and usable — without assembling an IM protocol from scratch or paying for a SaaS subscription.

---

## Highlights / 项目亮点

- 🔐 **Data ownership** — all users, chats and files live in your own directory; no cloud account, no telemetry.
- ⚡ **Realtime** — WebSocket push via SignalR: new messages, recall, joins, member changes sync instantly.
- 🛡️ **Security hardening (built-in, zero-config)** — traffic firewall (per-IP concurrency + rate + bandwidth, auto-ban), per-identity message-flood cap, httpOnly refresh tokens, dual slider + image captcha, isolated web honeypot + canaries, per-entrypoint group/message/conversation authorization, sensitive-word filter, loopback + key-gated admin API.
- 🎨 **WeChat-like UX** — conversation list, group-member grid, **pat-a-pat** (in private *and* group chats, with a one-tap menu entry), quote reply, @member / @everyone, dark mode, font personalization, offline Service Worker cache.
- 🖥️ **Admin GUI** — WPF tray-resident tool: start/stop the service, change port, view online users, tweak security policy, manage users, read logs / feedback.
- 👤 **WeChat-like account system** — register directly or via email verification code (SMTP configurable in the admin GUI); a randomly-generated 大面号 is unsearchable by default (mirrors WeChat's `wxid`, anti mass-add), changeable to a custom one with a configurable cooldown; "ways to add me" privacy toggles.

---

## Features / 功能总览

### Messaging
- 1v1 private + multi-person group chats; text, images (aspect-fit + preview), voice, files;
- **recall / quote-reply / save / delete** messages;
- **pat-a-pat in both private and group chats** — double-tap the other avatar, or use the explicit "Pat" item in the message right-click / long-press menu (no click-vs-double-click ambiguity); @member / @everyone, group announcement + confirmation receipt;
- **QQ-style group join & management**: join by group number with four policies (open / answer-question / apply-with-reason / closed), brute-force-limited join questions; **mute-all and per-member timed mute (10 min ~ permanent)**; rejection with optional reason; owner / admin tiered permissions;
- **Group polls**: poll cards with single/multi choice, live vote bars, closable;
- pin / mute / collapse conversations; multi-device IndexedDB cache + offline fallback.

### Profile & Social
- register / login with dual captcha + strength policy + failure lockout; **email registration** (SMTP code, admin-configured) or direct account registration;
- **大面号 (custom ID) system**: random unsearchable default, changeable with a configurable cooldown; bind / re-bind email with verification codes;
- **Search privacy ("ways to add me")**: independent toggles for being found by phone number or by custom ID;
- avatar, nickname, signature, gender, region, phone-privacy toggle;
- contacts + friend relationships, member profile cards;
- **Moments**: post text/media, like and comment;
- personalization: theme color, dark mode, translucency, wallpaper, font size (S/M/L/XL, persists across refresh).

### Admin
- real-time online users, ban / unban (instant disconnect), create / delete users (cascading cleanup), reset password (instant token revocation);
- user and group management, announcements / confirmations;
- hot-tweak security policy (rate limits, ban thresholds, honeypot switch, registration mode / key / SMTP, ID-change cooldown) with a one-click SMTP test email;
- feedback collection + deletion, log / console viewing.

---

## Docs / 文档

| Doc | What's inside |
|---|---|
| [📸 Feature Gallery (EN)](docs/feature-gallery.md) | **every screen with a real screenshot** (this page's images, expanded) |
| [📸 功能一览 (中文)](docs/功能一览.md) | 每个功能界面配实跑截图 |
| [📖 部署与使用指南 (中文)](docs/部署与使用指南.md) | **zero to live**: 3 deploy paths, HTTPS, first account, admin-GUI ops, troubleshooting, config reference |
| [中文 README](./README.zh.md) | 功能 / 技术栈 / 目录结构 / 安全说明（简中） |
| [Release binaries](https://github.com/Damianjiang/damian-chat/releases) | Windows x64 one-click package (bundled frontend + admin GUI) |

---

## Quick start / 快速开始

**Prerequisites**: Windows (the admin GUI is WPF; the backend core is cross-platform, only the GUI is Windows-only); **.NET 8 SDK** (`dotnet --version`); a browser.

**Option 1 · One-click (recommended)**
```bat
启动.bat
```
Compiles & launches the GUI admin (if not built) → admin auto-starts the backend → prints the LAN address. Closing the window just minimizes it to the tray.

**Option 2 · Run the backend directly**
```bat
cd server\WeChatWeb
dotnet run
```

**Option 3 · Docker (Linux / servers / cross-platform)**
```bash
docker build -t damian-chat .
docker run -d --name damian-chat \
  -p 8080:8080 \
  -v damian-chat-data:/app/data \
  -v damian-chat-logs:/app/logs \
  damian-chat
# → http://localhost:8080
```

Then open `http://<lan-ip>:<port>`. First launch creates `data/` with locally generated keys. Enable HTTPS with `WX_HTTPS_CERT` (PFX) + `WX_HTTPS_CERT_PASSWORD` + `WX_HTTPS_PORT`.

---

## Tech stack / 技术栈

| Layer | Tech |
|---|---|
| Backend | C# / .NET 8 · ASP.NET Core · SignalR · JWT · custom middleware |
| Frontend | Vue 3 · Vuetify 4 · Pinia · TypeScript · Vite |
| Realtime | @microsoft/signalr (WebSocket) |
| Media | IndexedDB blob cache (client) · server file store |
| Admin GUI | WPF (.NET 8 Windows) · tray-resident |

> The frontend bundle is emitted straight into the backend `wwwroot` — **at runtime you only need the .NET backend; no Node process required**.

## Security (built-in) / 安全说明

Everything below is **built-in and needs no extra configuration** — see the full list in [README.en.md](./README.en.md) / [README.zh.md](./README.zh.md):

- Data never leaves your machine · keys generated locally on first run · login anti-brute-force · rate limiting + DDoS defense · long-lived creds in httpOnly cookie · per-entrypoint member authorization · sensitive-word filter + loopback-gated admin API · isolated web honeypot · instant ban/delete disconnect.

## License

Released under the [MIT License](./LICENSE).
