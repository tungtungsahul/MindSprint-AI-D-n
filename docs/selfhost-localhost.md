# Localhost self-host setup

This repository now includes a laptop-specific Docker setup that keeps the local configuration separate from the shared Development and Render settings.

## 1. Create the local environment file

```powershell
# Windows PowerShell
./scripts/setup-selfhost.ps1
```

```bash
# Linux/macOS
bash ./scripts/setup-selfhost.sh
```

The script only creates `.env.selfhost` when it does not already exist; it never overwrites a valid local config.

## 2. Update values

Edit `.env.selfhost` and replace the placeholders for:

- `MSSQL_SA_PASSWORD`
- `Jwt__Key`
- `Gemini__ApiKey`
- `Google__ClientId`
- `Cors__AllowedOrigins`

Keep the default values safe, local-only, and isolated from the production Render configuration.

## 3. Run the local stack

```bash
docker compose --env-file .env.selfhost -f docker-compose.selfhost.yml config
# or
# docker compose --env-file .env.selfhost -f docker-compose.selfhost.yml up --build
```

Available endpoints:

- Frontend: http://127.0.0.1:8080
- API: http://127.0.0.1:5000
- SQL Server: 127.0.0.1:1433

The compose file binds all published ports to localhost only, so they are not exposed on every network interface.

## 4. Secrets and cleanup

- `.env.selfhost` is ignored by Git and Docker.
- `docker-compose.selfhost.yml` uses a dedicated Docker volume: `mindsprint_local_mssqldata`.
- Required values are enforced via `${VAR:?message}` so missing config fails fast instead of silently falling back to weak defaults.
- The app is kept in the `SelfHost` environment without reloading an override file after environment variables are already set.
