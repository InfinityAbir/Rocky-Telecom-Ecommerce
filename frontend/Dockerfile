# syntax=docker/dockerfile:1
FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM node:22-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production PORT=4000
COPY --from=build /app/dist ./dist
COPY --from=build /app/package.json ./package.json
# The SSR bundle only needs the runtime packages that server.mjs imports at start-up.
COPY --from=build /app/node_modules ./node_modules
RUN addgroup -S rocky && adduser -S rocky -G rocky
USER rocky
EXPOSE 4000
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD wget -qO- http://127.0.0.1:4000/healthz || exit 1
CMD ["node", "dist/rocky-telecom-web/server/server.mjs"]
