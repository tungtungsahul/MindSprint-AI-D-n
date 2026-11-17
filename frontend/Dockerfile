FROM node:20-alpine AS build
WORKDIR /src
COPY package.json build.mjs ./
COPY . .
ARG API_BASE=http://127.0.0.1:8080
RUN API_BASE="$API_BASE" npm run build

FROM nginx:1.27-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist/ /usr/share/nginx/html/
EXPOSE 80
HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=5 CMD wget -q -O /dev/null http://127.0.0.1/nginx-health || exit 1