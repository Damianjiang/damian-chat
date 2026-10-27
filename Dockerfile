# ============================================================================
# 大面聊天 · Damian Chat — 多阶段构建
#   阶段 1 (web)  : 构建 Vue 3 + Vuetify 前端 → 输出到后端 wwwroot
#   阶段 2 (api)  : 发布 .NET 8 后端（含前端产物，自包含静态托管）
#   阶段 3 (run)  : 运行时（aspnet:8.0），仅需此镜像
#
# 构建:
#   docker build -t damian-chat .
# 运行 (数据/日志持久化到宿主机卷):
#   docker run -d --name damian-chat \
#     -p 8080:8080 \
#     -v damian-chat-data:/app/data \
#     -v damian-chat-logs:/app/logs \
#     damian-chat
#   → http://localhost:8080
# ============================================================================

# ---- Stage 1: 前端构建 ----
FROM node:20-alpine AS web
WORKDIR /src
# 保留 server/ 与 web/ 两级目录：vite 的 outDir 是相对 web 的 ../server/WeChatWeb/wwwroot
COPY web/package.json web/package-lock.json ./web/
RUN cd web && npm ci
COPY web/ ./web/
RUN cd web && npm run build-only

# ---- Stage 2: 后端发布 ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS api
WORKDIR /src
COPY server/WeChatWeb ./server/WeChatWeb
# 把前端构建产物并入后端 wwwroot（发布时随静态文件一起打包）
COPY --from=web /src/server/WeChatWeb/wwwroot ./server/WeChatWeb/wwwroot
RUN dotnet publish server/WeChatWeb/WeChatWeb.csproj -c Release -o /out --nologo

# ---- Stage 3: 运行时 ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=api /out ./

# 默认监听 8080；可用环境变量覆盖
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

# 运行时生成 data/（用户数据 / 密钥 / 文件）与 logs/
VOLUME ["/app/data", "/app/logs"]

ENTRYPOINT ["dotnet", "WeChatWeb.dll"]
