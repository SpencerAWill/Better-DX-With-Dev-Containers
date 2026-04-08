# Dev Containers in a Monorepo

A demonstration of how to build and manage **dev containers** in a polyglot monorepo environment. The project constructs a fully containerized online ordering platform to showcase using multiple dependencies and sidecar containers that emulate production services — all while staying completely isolated on your local machine.

## What This Demonstrates

- **Dev container configuration** with Docker Compose for multi-service orchestration
- **Sidecar containers** (PostgreSQL, Azure Service Bus emulator) running alongside the development environment
- **Multi-language support** (TypeScript + C#) within a single dev container
- **Nx-style monorepo layout** with `apps/` and `libs/` for clear separation of concerns
- **Production-like dependency isolation** without polluting the host machine

## Repository Structure

```
.
├── .devcontainer/                 # Dev container configuration
│   ├── devcontainer.json          # VS Code / IDE dev container settings
│   ├── Dockerfile                 # Custom dev environment image
│   └── docker-compose.yml         # Service orchestration (app + sidecars)
├── apps/
│   ├── functions/                 # Azure Functions (C# / .NET 10 isolated worker)
│   │   ├── HealthCheck.cs         # HTTP-triggered health check function
│   │   ├── Program.cs             # Functions host entry point
│   │   └── Project.Functions.csproj
│   ├── webapi/                    # ASP.NET Core API (C# / .NET 10)
│   │   ├── Endpoints/             # Checkout & Stripe webhook endpoints
│   │   ├── Program.cs             # Application entry point
│   │   └── Project.API.csproj
│   └── webapp/                    # React frontend (TypeScript / Vite)
│       ├── src/
│       │   ├── components/        # UI components (Radix-based)
│       │   ├── routes/            # File-based routing (TanStack Router)
│       │   ├── hooks/             # Custom React hooks
│       │   └── integrations/      # Third-party integrations
│       ├── package.json
│       └── vite.config.ts
├── libs/
│   ├── data-models/               # Shared EF Core DbContext & entities
│   └── data-migrations/           # EF Core database migrations
├── Project.slnx                   # .NET solution file
├── pnpm-workspace.yaml            # PNPM workspace configuration
└── package.json                   # Root workspace tooling
```

## Tech Stack

| Layer               | Technology                                                  |
| ------------------- | ----------------------------------------------------------- |
| **Frontend**        | React 19, TanStack Router & Query, Vite, Tailwind CSS 4     |
| **Backend**         | ASP.NET Core (.NET 10), Stripe SDK                          |
| **Functions**       | Azure Functions v4 (.NET 10 isolated worker)                |
| **Database**        | PostgreSQL 17 (sidecar container), Entity Framework Core 10 |
| **Messaging**       | Azure Service Bus (emulated via sidecar container)          |
| **Dev Environment** | Dev Containers, Docker Compose, PNPM workspaces             |
| **Code Quality**    | ESLint, Prettier, `dotnet format`, Husky, Commitlint        |

## Getting Started

### Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (or any OCI-compliant container runtime)
- [VS Code](https://code.visualstudio.com/) with the [Dev Containers extension](https://marketplace.visualstudio.com/items?itemName=ms-vscode-remote.remote-containers), or any IDE with dev container support

### Open in Dev Container

1. Clone the repository
2. Open the folder in VS Code
3. When prompted, click **"Reopen in Container"** (or run the command `Dev Containers: Reopen in Container`)
4. The container builds with all dependencies — Node.js, .NET SDK, PostgreSQL sidecar, and CLI tools — ready to go

Everything is configured automatically. No local SDK installs required.

### Running the Apps

**Frontend (webapp):**

```bash
cd apps/webapp
pnpm dev
# → http://localhost:5173
```

**Backend (webapi):**

```bash
cd apps/webapi
dotnet run
# → http://localhost:5258 (HTTP)
# → https://localhost:7130 (HTTPS)
```

**Azure Functions:**

```bash
cd apps/functions
func start
# → http://localhost:7071
```

**Database migrations:**

Use the preconfigured VS Code tasks (`Terminal → Run Task`):

- **EF: Add Migration** — scaffold a new migration
- **EF: Update Database** — apply pending migrations
- **EF: Remove Last Migration** — remove the last migration

### Ports

| Port | Service                     |
| ---- | --------------------------- |
| 5173 | Vite dev server (webapp)    |
| 5258 | ASP.NET Core HTTP (webapi)  |
| 7130 | ASP.NET Core HTTPS (webapi) |
| 7071 | Azure Functions (functions) |
| 5432 | PostgreSQL                  |

## Dev Container Architecture

The dev container setup uses Docker Compose to orchestrate multiple services:

```
┌─────────────────────────────────────────────────┐
│  Docker Compose                                 │
│                                                 │
│  ┌─────────────────────────────────────────────────┐  │
│  │  devcontainer (primary)                         │  │
│  │  ┌──────────┐ ┌──────────────┐ ┌─────────────┐  │  │
│  │  │ Node.js  │ │  .NET SDK    │ │ Azure Func  │  │  │
│  │  │ (webapp) │ │ (webapi+libs)│ │ (functions) │  │  │
│  │  └──────────┘ └──────────────┘ └─────────────┘  │  │
│  └─────────────────────────────────────────────────┘  │
│                       │                               │
│              ┌────────┴────────┐                      │
│              ▼                 ▼                      │
│  ┌───────────────────┐ ┌─────────────────────────┐   │
│  │  postgres          │ │  servicebus-emulator    │   │
│  │  PostgreSQL 17     │ │  Azure Service Bus      │   │
│  │  persistent volume │ │  (backed by MSSQL)      │   │
│  └───────────────────┘ └─────────────────────────┘   │
└───────────────────────────────────────────────────────┘
```

The **sidecar pattern** means dependencies like PostgreSQL and the Azure Service Bus emulator run as separate containers managed by Docker Compose, connected over an internal network. This mirrors a production topology where databases and message brokers are separate services, while keeping everything local and disposable.

## Monorepo Conventions

This repo follows an Nx-inspired layout:

- **`apps/`** — Deployable applications (each with its own build/run configuration)
- **`libs/`** — Shared libraries consumed by apps (data models, migrations, utilities)
- **Root config** — Workspace-wide tooling (linting, formatting, git hooks)

The TypeScript side uses **PNPM workspaces** and the C# side uses a **Visual Studio solution file** (`.slnx`) to manage projects within their respective ecosystems.

## Git Workflow

The repo enforces quality through git hooks:

- **Commit messages** must follow [Conventional Commits](https://www.conventionalcommits.org/) (enforced by Commitlint)
- **Pre-commit** runs lint-staged: ESLint + Prettier for JS/TS, `dotnet format` for C#
- Run `pnpm commit` (or `npm run commit`) to use Commitizen for guided commit messages
