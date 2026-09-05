# syntax=docker/dockerfile:1

FROM node:24-alpine AS build
WORKDIR /src

RUN corepack enable && corepack prepare pnpm@11.5.1 --activate

COPY frontend/package.json frontend/pnpm-lock.yaml frontend/pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile

COPY frontend/ ./

ARG VITE_STRIPE_PUBLISHABLE_KEY
ARG VITE_CONTACT_EMAIL
ARG VITE_GOOGLE_CLIENT_ID
ARG VITE_APPLE_CLIENT_ID
ARG VITE_PUBLIC_URL
RUN pnpm build

FROM nginxinc/nginx-unprivileged:1.27-alpine AS runtime

ENV BACKEND_HOST=api \
    BACKEND_PORT=8080 \
    IS_STAGING=""

COPY frontend/nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /src/dist /usr/share/nginx/html

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget -q --spider http://localhost:8080/ || exit 1
