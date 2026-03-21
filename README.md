# EU5 Map Explorer

Monorepo for a sample application with:

- .NET 8 Web API backend
- PostgreSQL via Docker Compose
- Angular frontend (SCSS, standalone components)

## Prerequisites

- .NET SDK 8.x
- Docker Desktop
- Node.js 20+ (Node 24 LTS recommended)

## Getting started

### 1. Start PostgreSQL

```bash
docker compose up -d
```

The database will be available on `localhost:5432` with credentials from `.env.example` (copy to `.env` to override).

### 2. Run the backend API

```bash
cd backend/EU5MapExplorer.Api
dotnet run
```

The API listens on `http://localhost:5114` by default.

### 3. Run the Angular frontend

```bash
cd frontend/eu5-map-explorer
npm start
```

The app runs on `http://localhost:4200` and proxies `/api` calls to the backend at `http://localhost:5114`.

