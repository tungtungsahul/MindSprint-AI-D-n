# CI/CD Setup Guide for MindSprint

## Overview
This project uses GitHub Actions for CI/CD with:
- **Backend (ASP.NET Core)** → Render (staging & production)
- **Frontend (Static HTML/CSS/JS)** → Vercel (staging & production)

## Required Secrets

Go to **GitHub Repository → Settings → Secrets and variables → Actions** and add:

### Render (Backend)
| Secret | Description | How to get |
|--------|-------------|------------|
| `RENDER_SERVICE_ID` | Your Render Web Service ID | Render Dashboard → Service → Settings → Service ID |
| `RENDER_API_KEY` | Render API Key | Render Dashboard → Account Settings → API Keys |

### Vercel (Frontend)
| Secret | Description | How to get |
|--------|-------------|------------|
| `VERCEL_TOKEN` | Vercel Access Token | Vercel Dashboard → Settings → Tokens → Create |
| `VERCEL_ORG_ID` | Vercel Organization ID | `vercel inspect <deployment-url>` or Vercel CLI |
| `VERCEL_PROJECT_ID` | Vercel Project ID | Vercel Dashboard → Project → Settings → General |

## Setup Steps

### 1. Render (Backend)
1. Create a **Web Service** on Render
2. Connect to this GitHub repo
3. Build Command: `dotnet publish backend/MindSprint.Api -c Release -o ./publish`
4. Start Command: `dotnet ./publish/MindSprint.Api.dll`
5. Add Environment Variables in Render Dashboard:
   - `ConnectionStrings__Default` - Production SQL Server connection string
   - `Jwt__Key` - Strong secret key (32+ chars)
   - `Jwt__Issuer` - `MindSprint`
   - `Jwt__Audience` - `MindSprintClient`
   - `Jwt__AccessMinutes` - `15`
   - `Gemini__ApiKey` - Your Gemini API key
   - `ASPNETCORE_ENVIRONMENT` - `Production`
   - `Cors__Origins` - `https://your-frontend.vercel.app`

### 2. Vercel (Frontend)
1. Import project from GitHub on Vercel
2. Framework Preset: **Other** (static site)
3. Build Command: `echo "Static site - no build"`
4. Output Directory: `frontend`
4. Add Environment Variable:
   - `API_BASE` - `https://your-backend.onrender.com` (Render URL)

### 3. Get Vercel IDs
```bash
# Install Vercel CLI
npm i -g vercel

# Login
vercel login

# Link project (run from frontend folder)
cd frontend
vercel link

# Get IDs
vercel inspect <deployment-url> --token=<your-token>
# Look for "orgId" and "projectId"
```

## Workflow Triggers

| Event | Action |
|-------|--------|
| Push to `main` | Auto-deploy to **staging** (Render preview + Vercel preview) |
| Manual workflow dispatch with `deploy_production=true` | Deploy to **production** |

## Environment URLs

After setup:
- **Staging Backend**: `https://<your-service>.onrender.com`
- **Staging Frontend**: `https://mindsprint-staging.vercel.app`
- **Production Backend**: Same Render service with production env vars
- **Production Frontend**: `https://mindsprint.vercel.app`

## Database Migrations

Render will run migrations automatically on startup if you add to `Program.cs`:
```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}
```

## Local Testing

```bash
# Backend
cd backend/MindSprint.Api
dotnet run

# Frontend (serve static files)
cd frontend
npx serve -l 5500
```

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Render deploy fails | Check logs in Render Dashboard → Events |
| Vercel deploy fails | Check `vercel logs` or Vercel Dashboard |
| CORS errors | Ensure `Cors__Origins` includes frontend URL exactly |
| DB connection fails | Verify connection string format for SQL Server on Render |
| JWT errors | Ensure `Jwt__Key` is same on all environments |

## Rollback

- **Render**: Dashboard → Deploys → Rollback to previous
- **Vercel**: Dashboard → Deployments → Promote previous to production