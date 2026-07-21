# Build context is frontend/, so package.json and projects/shared are
# reachable (player-client imports "shared" via a dist/shared path mapping,
# built first). Build with:
#   docker build -f projects/player-client/Dockerfile .
# from frontend/, or context: ../../frontend / dockerfile: projects/player-client/Dockerfile
# in docker-compose.

FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

FROM nginx:1.27-alpine AS final
COPY --from=build /app/dist/player-client/browser /usr/share/nginx/html
COPY projects/player-client/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 8080
