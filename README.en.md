# Damian Chat (大面聊天)

A **self-hosted, WeChat-style instant messaging platform**: real-time group & private chat, Moments, rich media, QQ-style join-by-group-number, and a hardened security layer. **C# ASP.NET Core 8 + SignalR** on the backend, **Vue 3 + Vuetify 4 + TypeScript** on the frontend. No third-party SaaS — it just runs on your machine.

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](./LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![Vue](https://img.shields.io/badge/Vue-3.5-42b883?logo=vue.js)](https://vuejs.org)
[![TypeScript](https://img.shields.io/badge/TypeScript-5.9-blue)](https://www.typescriptlang.org)
[![Docker](https://img.shields.io/badge/Docker-ready-2496ED?logo=docker)](https://docs.docker.com)

> 中文版本 / Chinese version: [README.zh.md](./README.zh.md)

## Screenshots

<p align="center">
  <img src="docs/chat.png" width="440" alt="Chat list">
  <img src="docs/group.png" width="440" alt="Group chat & settings">
</p>

<p align="center">
  <img src="docs/private.png" width="440" alt="Private chat">
  <img src="docs/moments.png" width="440" alt="Moments">
</p>

<p align="center">
  <img src="docs/contacts.png" width="440" alt="Contacts">
  <img src="docs/dark.png" width="440" alt="Dark mode">
</p>

## Highlights

- 🔐 **Data ownership** — all users, chats and files live in your own directory; no cloud account, no telemetry.
- ⚡ **Realtime** — WebSocket push via SignalR: new messages, recall, joins and member changes sync instantly.
- 🛡️ **Security hardening** — traffic firewall (per-IP concurrency + rate + auto-ban), per-identity message-flood cap, httpOnly refresh tokens, dual slider + image captcha, isolated web honeypot, per-entrypoint group/message/conversation authorization, sensitive-word filtering, loopback + key gated admin API.
- 🎨 **WeChat-like UX** — conversation list, group-member grid, "pat a pat", quote reply, @member / @everyone, dark mode, font-size personalization, offline Service Worker cache.
- 🖥️ **Admin GUI** — a WPF tray-resident tool: start/stop the service, change the port, view online users, tweak security policy, manage users, read logs / feedback.

## Features

### Messaging
- 1v1 private + multi-person group chats; text, images (aspect-fit + click-to-preview), voice, files;
- recall / quote-reply / save / delete messages;
- group "pat a pat", @member, @everyone, group announcement + confirmation receipt;
- **QQ-style join-by-group-number**: open groups join directly, approval groups wait for the owner; group numbers are one-click copyable;
- pin / mute / collapse conversations; multi-device IndexedDB cache + offline fallback.

### Profile & Social
- register / login with dual captcha + strength policy + failure lockout;
- avatar, nickname, signature, gender, region, phone-privacy toggle;
- contacts + friend relationships, member profile cards;
- **Moments**: post text/media, like and comment;
- personalization: theme color, dark mode, translucency, wallpaper, font size (S/M/L/XL, persists across refresh).

### Admin
- real-time online users, ban / unban;
- user and group management, announcements / confirmations;
- hot-tweak security policy (rate limits, ban thresholds, honeypot switch);
- feedback collection + deletion, log / console viewing.

## Tech stack

| Layer | Tech |
|---|---|
| Backend | C# / .NET 8 · ASP.NET Core · SignalR · JWT · custom middleware |
| Frontend | Vue 3 · Vuetify 4 · Pinia · TypeScript · Vite |
| Realtime | @microsoft/signalr (WebSocket) |
| Media | IndexedDB blob cache (client) · server file store |
| Admin GUI | WPF (.NET 8 Windows) · tray-resident |
| Build | .NET SDK · Vite 7 · vue-tsc |

> The frontend bundle is emitted straight into the backend `wwwroot` — **at runtime you only need the .NET backend; no Node process required**.

## Repository layout

```
.
├── server/                  backend .NET solution
│   ├── WeChatWeb/           main service (controllers / hubs / middleware / services)
│   │   ├── Controllers/     REST API (auth / users / conversations / groups / moments / files / admin …)
│   │   ├── Hubs/            SignalR ChatHub (realtime push / online-connection management)
│   │   ├── Middleware/      firewall / rate limit / honeypot / logging / security headers …
│   │   ├── Services/        JWT / keys / captcha / sensitive words / honeypot / feedback / watchdog …
│   │   ├── Data/            data layer (DataStore / flood limiter, persisted to data/store.json)
│   │   └── Models/          entities (User / Group / Message / Moment …)
│   ├── WxAdminGui/          WPF GUI admin (tray-resident)
│   ├── WxAdmin/             CLI admin tool
│   └── Shared/              shared (admin-key reader)
├── web/                     frontend Vue 3 project
│   ├── src/
│   │   ├── views/           login + main-shell panes
│   │   ├── components/      message bubble / input toolbar / slider captcha / avatar …
│   │   ├── stores/          Pinia: auth / conversations / contacts / moments / theme
│   │   ├── lib/             api / signalr / idb / files / theme / personalize …
│   │   └── styles/          WeChat-style SCSS + Vuetify themes
│   └── public/              Service Worker / boot script / PWA manifest
├── docs/                    README screenshots
├── 启动.bat                 one-click start (launch admin → start service)
└── 停止.bat                 one-click stop (release the configured port)
```

## Quick start

**Prerequisites**: Windows (the admin GUI is WPF; the backend core is cross-platform, only the GUI is Windows-only); .NET 8 SDK (`dotnet --version`); open `http://<lan-ip>:<port>` in a browser.

**Option 1 · One-click (recommended)**
```bat
启动.bat
```
It compiles & launches the GUI admin (if not built) → the admin auto-starts the backend → prints the LAN address. Closing the window just minimizes it to the tray.

**Option 2 · Run the backend directly**
```bat
cd server\WeChatWeb
dotnet run
```

**Build the frontend (optional, bundled by default)**
```bat
cd web
npm install
npm run build-only     # emits to server\WeChatWeb\wwwroot
```

**Stop**
```bat
停止.bat
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
Data/keys/files live in the `data` volume, logs in the `logs` volume (survive restarts). Override the port with `ASPNETCORE_URLS`; enable HTTPS with `WX_HTTPS_CERT` (PFX) + `WX_HTTPS_CERT_PASSWORD` + `WX_HTTPS_PORT`.

> On first run a `data/` folder with locally generated keys is created (excluded from the repo).

## Security notes

Everything below is **built-in and needs no extra configuration**:

- **Data never leaves your machine** — users, chats and files stay on disk in your `data/`; no cloud accounts, no telemetry, no third-party callbacks.
- **Keys generated locally on first run** — JWT and admin keys are randomly generated and stored locally; no usable secret is ever shipped in source or the repo.
- **Login anti-brute-force** — dual slider + image captcha, lockout on repeated failure, replaceable background-image pools; PBKDF2 salted password hashing + strength policy.
- **Rate limiting & DDoS defense** — per-IP concurrency/rate caps with auto-ban, plus a per-identity message-flood limit so a stolen token + IP rotation can't bypass IP limits.
- **Long-lived creds in httpOnly cookie** — refresh tokens live in httpOnly cookies (XSS-resistant); access tokens expire in 2 hours.
- **Per-entrypoint authorization** — group IDs are non-enumerable GUIDs; non-members get 404/403 on every read/write; you can only add your own contacts; role changes are owner-only; join approvals are owner/admin-only.
- **Sensitive-word filter + loopback-gated admin API** — inbound sensitive-word stripping; `/api/admin/*` only accepts loopback requests **and** requires the local admin key.
- **Isolated web honeypot** — decoy `.env`/`.git`/admin console + canaries + tarpit delay; read-only, fully isolated from the real system, zero impact on real users.
- **Instant ban/delete disconnect** — authenticated requests re-check ban status by subject; a hit immediately aborts the live connection.

> **Production tips**: put it behind HTTPS (`WX_HTTPS_CERT`), keep the admin API loopback-only, and tune the rate/ban thresholds in `security.json` for your network.

## License

Released under the [MIT License](./LICENSE).
