FROM node:24.22.1-bookworm-slim@sha256:be8204d4bd327363357f532187b752ff62759c31bedf98e9e18fc08c9cbd605c AS node
FROM mcr.microsoft.com/dotnet/sdk:10.0.403-noble@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e
COPY --from=node /usr/local/ /usr/local/
RUN apt-get update && apt-get install -y --no-install-recommends \
    bzip2 unzip libasound2t64 libatk-bridge2.0-0t64 libatk1.0-0t64 libcups2t64 \
    libdrm2 libgbm1 libgtk-3-0t64 libnspr4 libnss3 libx11-xcb1 libxcomposite1 \
    libxdamage1 libxfixes3 libxrandr2 libxkbcommon0 libpango-1.0-0 libcairo2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /workspace/eng/visual
COPY eng/visual/package*.json ./
RUN npm ci --ignore-scripts && node node_modules/puppeteer/install.mjs
WORKDIR /workspace
ENV SVT_CHROME_NO_SANDBOX=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
CMD ["node", "eng/visual/release-render.mjs"]
