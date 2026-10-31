# 大面聊天 · Damian Chat

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](./LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![Vue](https://img.shields.io/badge/Vue-3.5-42b883?logo=vue.js)](https://vuejs.org)
[![TypeScript](https://img.shields.io/badge/TypeScript-5.9-blue)](https://www.typescriptlang.org)
[![Docker](https://img.shields.io/badge/Docker-ready-2496ED?logo=docker)](https://docs.docker.com)

**English · 英文**: [Read in English →](./README.en.md)　|　**中文 · Chinese**: [中文阅读 →](./README.zh.md)

> A self-hosted, WeChat-style chat platform with realtime group/private chat, Moments, and a hardened security layer. .NET 8 + SignalR backend, Vue 3 + Vuetify 4 + TypeScript frontend. 可自托管、类微信的即时通讯系统，开箱即用。

<p align="center">
  <img src="docs/chat.png" width="460" alt="Damian Chat — main UI / 聊天主界面">
</p>

## Why this project / 项目亮点

- 💬 **WeChat clone, self-hosted / 类微信·可自托管** — WeChat-style chat UI (private & group chat, Moments/朋友圈, pat, quote-reply, @mentions) that runs entirely on your own machine. No SaaS, no cloud accounts.
- 🏷️ **WeChat-like ID system / 微信式大面号** — email-code registration (SMTP) or direct account registration; random IDs are unsearchable by default (like WeChat's wxid); custom IDs with a configurable change cooldown (365 days, WeChat-style).
- 👥 **QQ-style group management / QQ 式群管理** — join by group number with 4 policies (open / question / apply / closed), mute-all, timed per-member mute, rejection reasons, group polls.
- 🛡️ **Hardened by design / 深度安全加固** — traffic firewall + auto-ban, dual captcha + slider, httpOnly refresh-token rotation, honeypot traps, per-entrypoint authorization, loopback-gated admin API.
- 🖥️ **Tray-resident admin GUI / 图形化管理端** — start/stop, ports, online users, security policy, SMTP, users, logs — all from a Windows WPF app.
- 🌐 **Bilingual UI / 中英双语界面** — one-click switch between Chinese and English.

**Search keywords / 关键词**: self-hosted chat, WeChat clone, WeChat alternative, open-source IM, private chat server, 局域网聊天, 自托管即时通讯, 微信开源替代, 私有部署聊天系统, SignalR chat, ASP.NET Core chat, Vue 3 chat app, offline-first messenger.

Full docs (screenshots, features, quick start, security notes) live in the two language-specific files above:
完整文档（界面截图、功能、快速开始、安全说明）在上述两个语言文件中：

- 🇬🇧 **English** — [README.en.md](./README.en.md)
- 🇨🇳 **中文** — [README.zh.md](./README.zh.md)
