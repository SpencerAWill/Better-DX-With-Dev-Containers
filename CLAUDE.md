# CLAUDE.md

## Project Overview

Polyglot monorepo demonstrating dev containers for an online ordering platform. Multi-language (TypeScript + C#) with sidecar containers (PostgreSQL, Azure Service Bus emulator, Azurite, Azure Cosmos DB emulator) emulating production dependencies.

## Repository Layout

- `apps/webapp/` — React 19 frontend (TanStack Router/Query, Vite, Tailwind CSS 4)
- `apps/webapi/` — ASP.NET Core API (.NET 10, Stripe integration)
- `apps/functions/` — Azure Functions (.NET 10, isolated worker) for background/event-driven processing
- `libs/data-models/` — Shared EF Core DbContext and entity definitions
- `libs/data-migrations/` — EF Core migration assemblies
- `.devcontainer/` — Dev container config (Dockerfile + Docker Compose with PostgreSQL, Service Bus emulator, Azurite & Cosmos DB emulator sidecars)

## Build & Run

```bash
# Frontend
cd apps/webapp && pnpm dev          # Vite dev server on :5173
cd apps/webapp && pnpm build        # Production build

# Backend
cd apps/webapi && dotnet run        # HTTP :5258, HTTPS :7130

# Azure Functions
cd apps/functions && func start     # Azure Functions local host on :7071

# EF Core migrations (from repo root)
dotnet ef migrations add <Name> --project libs/data-migrations --startup-project apps/webapi
dotnet ef database update --project libs/data-migrations --startup-project apps/webapi
```

## Test & Lint

```bash
# Frontend tests
cd apps/webapp && pnpm test         # Vitest

# Linting
cd apps/webapp && pnpm lint         # ESLint
cd apps/webapp && pnpm format       # Prettier check
cd apps/webapp && pnpm check        # Prettier write + ESLint fix

# C# formatting
dotnet format                       # Formats all .cs files in solution
```

## Code Style

### General

- EditorConfig enforced: 2-space indentation (default), UTF-8, LF line endings, trailing newline
- Prettier formats JS/TS/JSON/YAML/CSS/MD/shell files
- ESLint with TanStack config for TypeScript

### C# Specific

- 4-space indentation, CRLF line endings
- `dotnet format` runs on pre-commit via lint-staged
- Nullable reference types enabled, implicit usings enabled
- Braces on new lines (Allman style)

### TypeScript Specific

- Strict mode enabled, no unused locals/parameters
- Path aliases: `#/*` and `@/*` both resolve to `./src/*`
- File-based routing via TanStack Router (routes in `apps/webapp/src/routes/`)
- `routeTree.gen.ts` is auto-generated — do not edit manually

## Git Conventions

- **Conventional Commits** required (enforced by Commitlint + Husky)
- Use `pnpm commit` at repo root for guided commit message via Commitizen
- Pre-commit hook runs lint-staged (ESLint + Prettier for JS/TS, `dotnet format` for C#)

## Key Dependencies

- **Frontend:** React 19, TanStack (Router, Query, Form), Radix UI, Zod 4, Vite 7
- **Backend:** ASP.NET Core 10, Stripe.net, EF Core 10, Npgsql
- **Functions:** Azure Functions v4 (.NET isolated worker), Application Insights
- **Database:** PostgreSQL 17 (sidecar container)
- **Messaging:** Azure Service Bus (emulated via sidecar container)
- **Storage:** Azure Storage (emulated via Azurite sidecar container)
- **Package manager:** PNPM (workspaces) for JS, .NET CLI / `.slnx` solution for C#

## Dev Container

The dev container uses Docker Compose with:

- Primary container: Debian Bookworm base with Node.js + .NET SDK
- Sidecar: PostgreSQL 17 (`Host=postgres;Port=5432;Database=app;Username=postgres;Password=postgres`)
- Sidecar: Azure Service Bus emulator (backed by MSSQL) (`Endpoint=sb://servicebus-emulator;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;`)
- Sidecar: Azurite — Azure Storage emulator for Blob, Queue, and Table services (`DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;...;BlobEndpoint=http://azurite:10000/devstoreaccount1;QueueEndpoint=http://azurite:10001/devstoreaccount1;TableEndpoint=http://azurite:10002/devstoreaccount1;`)
- Sidecar: Azure Cosmos DB emulator (Linux) (`AccountEndpoint=https://cosmosdb:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QypfDERNfnKC0JV3rGK5rf8T3OZZhL/1YMYVRXQS97vDvKnDRQ==;`)
- Ports: 5173 (Vite), 5258 (HTTP API), 7130 (HTTPS API), 7071 (Azure Functions), 5432 (PostgreSQL), 8081 (Cosmos DB), 10000-10002 (Azurite)

## Documentation Maintenance

When making changes that affect the project structure, dependencies, build/run commands, dev container configuration, or any other information documented in this file or `/workspace/README.md`, **you must update both files** as part of the same change. Specifically:

- **Adding/removing/renaming an app or lib** — update Repository Layout here, Repository Structure in README, and the Tech Stack table
- **Adding/removing a sidecar container** — update Dev Container sections in both files and the architecture diagram in README
- **Changing ports, connection strings, or environment variables** — update the Ports table and Dev Container sections
- **Adding/changing build, run, test, or lint commands** — update Build & Run and Test & Lint sections
- **Adding/removing key dependencies or tooling** — update Key Dependencies here and Tech Stack in README
- **Changing code style rules, git conventions, or dev workflow** — update the relevant sections in both files

Keep the same voice and format already established in each file. CLAUDE.md is terse and reference-oriented; README.md is explanatory and onboarding-oriented.
