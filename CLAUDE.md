# CLAUDE.md

## Project Overview

Polyglot monorepo demonstrating dev containers for an online ordering platform. Multi-language (TypeScript + C#) with sidecar containers (PostgreSQL) emulating production dependencies.

## Repository Layout

- `apps/webapp/` — React 19 frontend (TanStack Router/Query, Vite, Tailwind CSS 4)
- `apps/webapi/` — ASP.NET Core API (.NET 10, Stripe integration)
- `libs/data-models/` — Shared EF Core DbContext and entity definitions
- `libs/data-migrations/` — EF Core migration assemblies
- `.devcontainer/` — Dev container config (Dockerfile + Docker Compose with PostgreSQL sidecar)

## Build & Run

```bash
# Frontend
cd apps/webapp && pnpm dev          # Vite dev server on :5173
cd apps/webapp && pnpm build        # Production build

# Backend
cd apps/webapi && dotnet run        # HTTP :5258, HTTPS :7130

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
- **Database:** PostgreSQL 17 (sidecar container)
- **Package manager:** PNPM (workspaces) for JS, .NET CLI / `.slnx` solution for C#

## Dev Container

The dev container uses Docker Compose with:

- Primary container: Debian Bookworm base with Node.js + .NET SDK
- Sidecar: PostgreSQL 17 (`Host=postgres;Port=5432;Database=app;Username=postgres;Password=postgres`)
- Ports: 5173 (Vite), 5258 (HTTP API), 7130 (HTTPS API), 5432 (PostgreSQL)
