# OrderingPlatform.TestingCommon

Shared test helpers referenced by every `tests/*.IntegrationTests` project.

- `TestEnvironment` — connection strings for dev-container sidecars (override via env vars)
- `PostgresDatabase` — per-test-class fresh database with Respawn-based reset between tests
- `TestWebApplicationFactoryBase<TProgram>` — base `WebApplicationFactory` wired to the test database

Not a test runner — `IsTestProject=false`. Referenced by other projects under `tests/`.
