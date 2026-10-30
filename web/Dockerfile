# ---- Deps ----
# Isolated stage so the npm install layer caches across source edits.
# COPY of package.json and package-lock.json only means a change to any
# source file does not invalidate this layer.
FROM node:20-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json* ./
RUN npm ci

# ---- Build ----
FROM node:20-alpine AS builder
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY . .

# Guarantee the public directory exists so the runner stage's
# COPY --from=builder /app/public succeeds even when the source has no
# tracked files under public/. Git does not track empty directories.
RUN mkdir -p public

# NEXT_PUBLIC_* variables are inlined at build time by Next.js, so they
# must be build args, not runtime environment. The value is the URL the
# browser reaches — the host's published API port.
ARG NEXT_PUBLIC_API_BASE_URL=http://localhost:8080
ENV NEXT_PUBLIC_API_BASE_URL=${NEXT_PUBLIC_API_BASE_URL}
ENV NEXT_TELEMETRY_DISABLED=1

RUN npm run build

# ---- Runtime ----
FROM node:20-alpine AS runner
WORKDIR /app

ENV NODE_ENV=production
ENV NEXT_TELEMETRY_DISABLED=1
ENV PORT=3000
ENV HOSTNAME=0.0.0.0

# Non-root runtime user — matches the pattern in the api and ai Dockerfiles.
RUN addgroup --system --gid 1001 nodejs \
 && adduser --system --uid 1001 nextjs

# Next.js standalone output. Requires output: 'standalone' in
# next.config.mjs — without it, .next/standalone does not exist and this
# COPY fails with "file does not exist".
COPY --from=builder --chown=nextjs:nodejs /app/public ./public
COPY --from=builder --chown=nextjs:nodejs /app/.next/standalone ./
COPY --from=builder --chown=nextjs:nodejs /app/.next/static ./.next/static

USER nextjs
EXPOSE 3000

CMD ["node", "server.js"]
