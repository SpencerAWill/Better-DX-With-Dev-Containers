# Testing Guide

How to write, run, and organize tests in this monorepo. The high-level strategy lives in the **Testing strategy** ADR in `docs/architecture/system-architecture.md`; this document is the operational how-to.

## Test tiers

| Tier             | Where                                                      | Runs                   | Purpose                                 |
| ---------------- | ---------------------------------------------------------- | ---------------------- | --------------------------------------- |
| **Unit**         | `tests/*.UnitTests/` (.NET), `src/**/*.test.ts` (frontend) | In-memory              | Pure logic, no I/O                      |
| **Integration**  | `tests/*.IntegrationTests/`                                | Dev-container sidecars | One service wired to real deps          |
| **Component**    | `apps/*/src/**/*.test.tsx`                                 | jsdom + MSW            | React components, hooks, routes         |
| **Architecture** | `tests/OrderingPlatform.ArchitectureTests/`                | In-memory (reflection) | Layering rules                          |
| **End-to-end**   | `e2e/`                                                     | Full stack             | User journeys (add as journeys wire up) |

Pick the lowest tier that proves what you need. If you can assert the behaviour in a unit test, do — unit tests are 100× faster and fail more precisely.

## Running tests

All commands assume you're inside the dev container. Integration tests connect to the Postgres / Service Bus / Event Hubs / Azurite / Cosmos / Redis / Keycloak sidecars defined in `.devcontainer/docker-compose.yml`.

```bash
# Everything (.NET)
dotnet test OrderingPlatform.slnx

# One project
dotnet test tests/OrderingPlatform.Ordering.Api.IntegrationTests

# One test class
dotnet test tests/OrderingPlatform.Ordering.Api.IntegrationTests \
  --filter "FullyQualifiedName~HostBootSmokeTests"

# Frontend (once)
cd apps/ordering-web && pnpm test

# Frontend (watch)
cd apps/ordering-web && pnpm test:watch
```

## Project naming

Test project names mirror the production assembly they cover, suffixed with the tier:

| Production assembly                       | Test project                                               |
| ----------------------------------------- | ---------------------------------------------------------- |
| `OrderingPlatform.Ordering.Api`           | `OrderingPlatform.Ordering.Api.UnitTests`                  |
| `OrderingPlatform.Ordering.Api`           | `OrderingPlatform.Ordering.Api.IntegrationTests`           |
| `OrderingPlatform.Ordering.Data`          | `OrderingPlatform.Ordering.Data.IntegrationTests`          |
| `OrderingPlatform.Notification.Functions` | `OrderingPlatform.Notification.Functions.IntegrationTests` |

The folder matches the csproj: `tests/OrderingPlatform.Ordering.Api.UnitTests/OrderingPlatform.Ordering.Api.UnitTests.csproj`.

## Adding a new test project

1. Create the folder under `tests/` with the full `{ProductionName}.{Tier}` name.
2. Create the `.csproj` — SDK `Microsoft.NET.Sdk`, target `net10.0`, `IsTestProject=true`, `IsPackable=false`.
3. Reference: `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `Shouldly`, `coverlet.collector`. Add `NSubstitute` for mocking, `OrderingPlatform.TestingCommon` for shared fixtures.
4. Add `GlobalUsings.cs` with the conventional globals (`Shouldly`, `Xunit`, relevant production namespace).
5. Register the project in `OrderingPlatform.slnx`.
6. For an architecture-covered assembly, register it in `OrderingPlatform.ArchitectureTests/LayeringTests.cs` (`LoadAllProductionAssemblies`).

## Fixture patterns

### `PostgresDatabase` — per-test-class Postgres

Creates a fresh database on first use, applies the caller's schema (`EnsureCreatedAsync` today, `MigrateAsync` once migrations exist), and drops the database on disposal. Use `ResetAsync` between tests to truncate tables without re-running migrations.

```csharp
public sealed class MyDatabaseFixture : PostgresDatabase
{
    public MyDatabaseFixture() : base(
        databaseName: $"test_mydomain_{Guid.NewGuid():N}",
        applyMigrations: ApplySchema) { }

    private static async Task ApplySchema(string connectionString)
    {
        var options = new DbContextOptionsBuilder<MyDbContext>().UseNpgsql(connectionString).Options;
        await using var ctx = new MyDbContext(options);
        await ctx.Database.EnsureCreatedAsync();
    }
}

public class MyTests : IClassFixture<MyDatabaseFixture>, IAsyncLifetime
{
    private readonly MyDatabaseFixture _db;
    public MyTests(MyDatabaseFixture db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();   // truncate before each test
    public Task DisposeAsync() => Task.CompletedTask;
}
```

xUnit calls `InitializeAsync`/`DisposeAsync` on the fixture once per class and on the test class once per test.

### `TestWebApplicationFactoryBase<TProgram>` — API integration tests

Boots an ASP.NET Core app in-memory and injects the test database's connection string into `IConfiguration["ConnectionStrings:Default"]`. Override `ConfigureTestServices` to swap DI registrations.

```csharp
public sealed class MyApiFactory : TestWebApplicationFactoryBase<Program>
{
    protected override string DatabaseConnectionString => /* from a PostgresDatabase fixture */;
    // optional: override ConfigureTestServices to swap a client, adapter, etc.
}
```

Production apps must expose `Program` so the generic resolves — add `public partial class Program;` at the bottom of `Program.cs`.

### MSW — frontend network mocks

Handlers live in `apps/ordering-web/src/test/msw-handlers.ts`. The setup file starts the server for all tests and resets handlers between tests. For per-test overrides:

```ts
import { server } from "./test/msw-server";
import { http, HttpResponse } from "msw";

it("handles a stale cart", async () => {
  server.use(
    http.get(
      "http://localhost:5258/cart",
      () => new HttpResponse(null, { status: 409 }),
    ),
  );
  // ...
});
```

Unhandled requests throw by default (`onUnhandledRequest: 'error'`) so tests can never silently hit the real network.

## Connection strings and overrides

`OrderingPlatform.TestingCommon.TestEnvironment` exposes each sidecar's connection string. Defaults match the compose file; override with env vars to run against a different host:

| Env var                          | Purpose                                   |
| -------------------------------- | ----------------------------------------- |
| `TEST_POSTGRES_HOST`             | Postgres hostname (default: `postgres`)   |
| `TEST_POSTGRES_ADMIN_CONNECTION` | Admin connection for create/drop database |
| `TEST_SERVICEBUS_CONNECTION`     | Service Bus emulator                      |
| `TEST_EVENTHUBS_CONNECTION`      | Event Hubs emulator                       |
| `TEST_AZURITE_CONNECTION`        | Azurite (Blob/Queue/Table)                |
| `TEST_REDIS_CONNECTION`          | Redis                                     |

## Do / don't

**Do:**

- Write a test at the lowest tier that proves what you need.
- Reset the database between tests via `PostgresDatabase.ResetAsync()` — never rely on test order.
- Name integration test databases with a per-run GUID to prevent cross-run collisions.
- Add new production assemblies to `LayeringTests.LoadAllProductionAssemblies` so architecture rules cover them.

**Don't:**

- Mock what you own if you can test against the real thing cheaply (dev-container sidecars are the real thing).
- Share state between tests through static fields or a shared database without Respawn.
- Reference test helpers from production code — `tests/*` cannot appear in a `ProjectReference` from `apps/*` or `libs/*`.
- Introduce Testcontainers for standard infra — the dev-container sidecars already cover it.
