# CLAUDE.md

## Project Overview

VetSystem Backend is the .NET 10 API for the Integrated Veterinary Practice Management System — an Arabic-first platform for veterinary centers in the West Bank, Palestine that combine in-clinic care with field visits to farms and homes. It handles staff auth (email/phone + password, admin-approval registration), pets and medical records, appointments, POS, central + field inventory, contracts and farm batches, doctor entitlements and settlement, customer ledgers, reporting, and the offline-sync **write path** for both clients.

## Tech Stack

| Layer | Technology |
|---|---|
| Framework | ASP.NET Core 10 Web API — **Minimal APIs** (route groups per resource, **not** Controllers) |
| Architecture | Clean / Onion: `API → Application → Domain ← Infrastructure` |
| ORM | EF Core 10 + Npgsql |
| Database | PostgreSQL 17 — **no PostGIS** (no geospatial needs) |
| Offline sync | PowerSync (self-hosted) reads Postgres via logical replication; **all writes flow through this API** |
| Mapping | Mapster (source-generated, `IRegister`) — **not** AutoMapper |
| Validation | FluentValidation |
| Auth | JWT + refresh token + BCrypt (custom, not ASP.NET Identity); admin-approval workflow |
| Realtime | SignalR — single `NotificationsHub`, **in-process (no Redis backplane)** |
| Background jobs | Hangfire (vaccination reminders, stock/expiry alerts, settlement notifications, scheduled reports) |
| Caching | `IMemoryCache` (in-process) — **no Redis**; rate limiting via the built-in ASP.NET Core limiter |
| Object storage | Cloudflare R2 (S3-compatible via AWS SDK) — private bucket, signed URLs only |
| API docs | NSwag (OpenAPI spec consumed by the frontend's type-gen) |
| Logging | Serilog (structured, multi-sink) |
| Tests | xUnit + FluentAssertions + Moq + Bogus; integration tests against a PostgreSQL test container (not EF InMemory) |

## Local dev URLs

Dev ports (set in `src/VetSystem.API/Properties/launchSettings.json`):
- HTTPS: `https://localhost:7180`
- HTTP:  `http://localhost:5180`
- Swagger UI / spec: `http://localhost:5180/swagger` and `http://localhost:5180/swagger/v1/swagger.json` — the frontend's `openapi-typescript` type-gen consumes this same URL.
- Health: `http://localhost:5180/health/live`, `http://localhost:5180/health/ready`
- JWKS: `http://localhost:5180/.well-known/jwks.json` (read by the PowerSync Service)

## Code Rules

- Clean Architecture layering is one-way and strict. Flag any cross-layer leak before implementing; confirm with the user before bending the rule.
- `Domain` has **zero** external dependencies (no PostGIS / NetTopologySuite needed here). Refuse to add NuGet references to it.
- `Application` has no EF Core and no Infrastructure types — repository **interfaces** only. Mapping config lives in Mapster `IRegister` profiles.
- **Minimal APIs, not Controllers.** One endpoint module per resource registers a `RouteGroupBuilder` (e.g. `app.MapGroup("/contracts")`, `app.MapGroup("/sync")`), wired from `Program.cs`. Endpoints stay thin: validate → call an Application service returning `Result<T>` → `TypedResults`. SignalR hubs live in `API`.
- **Cross-cutting concerns are endpoint filters**, not per-endpoint code: FluentValidation, auth/permission checks, and idempotency-key handling via `AddEndpointFilter` on the group.
- Use `Result<T>` in Application; the global `ExceptionHandlingMiddleware` maps to HTTP. Error shape is `{ code, message, fieldErrors? }`.
- **The `/sync/{table}` write endpoints are the only path clients use to persist** (PowerSync's upload connector and the web offline queue both call them; records arrive with client-generated GUID v7 ids). They MUST enforce the server-side invariants on every write — see `docs/SCHEMA.md` "Key invariants": settlement lock, delta-only inventory (translate intents into signed `inventory_movements`, never write absolute quantities), append-only ledger/invoices, server-wins on financial/medication conflicts, idempotency key on every write.
- Store R2 object keys in DB columns; mint short-lived (5-min) signed URLs on read. Never store or return public URLs.
- Any value an admin can tune (exam fee, profit-distribution %, doctor-entitlement global toggle, low-stock threshold, expiration-warning days, tax enable/rate) must be editable in the Admin Configuration UI with no redeploy — these map to the `system_settings` table.
- Log errors with sufficient context (function name, relevant IDs) via Serilog.
- `main` is protected; always create a new branch for a task if you're not already on one.
- Commit format: `milestone N / sub-commit M: <change>` for milestone work, `milestone N / hotfix: <fix>` for milestone bugfixes, `pre-milestone hygiene: <change>` for cross-cutting cleanup that doesn't belong to a specific milestone. `N` references the milestone numbers in `docs/tasks.md` (M0–M14). Ask if the milestone number isn't obvious.
- When creating a PR, the base must be the `sandbox` branch.

## PowerSync — Backend Responsibilities

- The **read path** is handled entirely by the PowerSync Service (logical replication + Sync Rules). There is **no pull endpoint** to build.
- Build the **write path**: `POST /sync/{table}`, `PATCH /sync/{table}/{id}`, `DELETE /sync/{table}/{id}` (soft delete). All business rules are enforced here.
- Mint client tokens at `POST /auth/powersync-token` and expose a **JWKS URI** the PowerSync Service trusts.
- Postgres needs **logical replication** enabled (`wal_level=logical` + a publication). See `docs/TECH_STACK.md` for the Docker Compose / Sync-Rules setup.

### Verifying the replication slot on first `docker compose up`

After `docker compose up postgres powersync`, the PowerSync container should log a successful replication slot attachment. Expected lines (paraphrased — exact wording can vary between minor versions of `journeyapps/powersync-service`):

```
[powersync] Connected to PostgreSQL replication source
[powersync] Replication slot 'powersync' is active on publication 'powersync'
[powersync] Starting replication stream from LSN …
```

If the slot is **not** created, the most common causes are: (1) `wal_level` is not `logical` on Postgres, (2) the `powersync` publication was not created (`/db-init/00-publication.sql` did not run because the data volume pre-existed), or (3) `client_auth.jwks_uri` is unreachable from the container. The M14 RUNBOOK.md will own the operational version of this checklist.

## QA

- No CI yet — validate with `dotnet build` + `dotnet test` before claiming a milestone (or sub-commit) complete.
- Run the xUnit suite for any change to **doctor-entitlement calculations (both systems), exam-fee models, the settlement-lock rule, or inventory delta math** — these are the money-critical, error-prone paths.
- For endpoint changes, regenerate the NSwag spec so the frontend stays in sync.

## Important Rules

- Auto-migration is disabled. Run migrations with explicit project paths:
  ```bash
  dotnet ef database update \
    --project src/VetSystem.Infrastructure \
    --startup-project src/VetSystem.API
  ```
  The same `--project` / `--startup-project` pair applies to `migrations add` and `migrations script`. Run `dotnet tool restore` if `dotnet ef` is missing.
- Seed data: `dotnet run --project src/VetSystem.API -- --seed` (`--force-seed` to wipe and re-seed). Source of truth: `Persistence/DataSeeder.cs`.
- Solution file: use `VetSystem.slnx` (XML) — pass `--solution VetSystem.slnx` to `dotnet sln` commands.
- Hangfire + `Hangfire.PostgreSql` can pull a vulnerable `Newtonsoft.Json` transitive — if `dotnet list package --vulnerable` flags it, pin `Newtonsoft.Json` in `VetSystem.Infrastructure.csproj` (as done in CourierPlatform) and never downgrade the pin.
- `appsettings.json` ships with placeholders for the JWT secret, R2 credentials, and the PowerSync token-signing / JWKS key. Override via `dotnet user-secrets` (UserSecretsId `vet-system-secrets`) — never edit the file with real values.

## Operations

### Rate limiting (`/sync/*`)

`/sync/*` is rate-limited with a **per-user token bucket** (ASP.NET Core's built-in limiter; M13 task 10) to absorb the field-doctor reconnect/sync storms in PRD §14. The partition key is the authenticated user (connection IP as a fallback). Over-limit requests get **429** with the canonical `{ code: "rate_limited", message }` body and a `Retry-After` header.

Tuning knobs live in the `RateLimiting:Sync` config section (override via `appsettings.{Env}.json`, env vars, or user-secrets — no code change):

| Key | Default | Meaning |
|---|---|---|
| `TokenLimit` | `200` | Bucket capacity — the max burst one user may send. |
| `TokensPerPeriod` | `100` | Tokens added each replenishment period. |
| `ReplenishmentPeriodSeconds` | `10` | How often tokens replenish. |
| `QueueLimit` | `0` | Requests queued when the bucket is empty (`0` = reject immediately). |

`RateLimiting:Enabled` is the master switch: it defaults **off in the `Test` environment** (so the integration suite is never throttled) and **on** everywhere else; set it explicitly to force either way. The limiter middleware always runs — when disabled, the `sync` policy resolves to a no-op limiter, so it stays transparent. The flag and limits are read **per request** (not at host build) so tests/ops can toggle them without the eager-config pitfall that `VetApiFactory` documents.

### Logging & observability

Serilog, structured + multi-sink (configured in the `Serilog` section of `appsettings.json` + `Program.cs`):

- **Rolling daily file** at `logs/vet-system-.log`, 14 days retained.
- **Seq** when `Seq:ServerUrl` is set (override via env/user-secrets); unset = file only (dev default).
- Every event is enriched with `MachineName`, `ThreadId`, `EnvironmentName`; HTTP requests are summarised by `UseSerilogRequestLogging` (method, path, status, elapsed).

**Convention — always use message templates, never string interpolation**, so values land as queryable structured properties:

```csharp
_logger.LogInformation("Seeded bootstrap environment {EnvironmentId}", environmentId); // queryable
_logger.LogInformation($"Seeded environment {environmentId}");                          // AVOID
```

Include the relevant IDs (environment, user, entity) and the operation name as properties. Errors flow through `ExceptionHandlingMiddleware`, which logs unhandled exceptions, concurrency conflicts, and unique-violations with the request path before returning the canonical `{ code, message, fieldErrors? }`.

Operational signals (query in Seq; alert thresholds + a Sentry sink finalize with M13 task 8 / M14):

| Signal | Where | Why |
|---|---|---|
| `@Level = 'Error'` | Seq | unhandled errors / 500s |
| `rate_limited` 429s | Seq (request log) | field-doctor sync-storm pressure (tune `RateLimiting:Sync`) |
| Job failures | `/hangfire` dashboard (Admin-gated) | reminders / alerts / report delivery health |
| `/health/ready` `status` | uptime monitor | `degraded` ⇒ Hangfire or PowerSync slot down; `503` ⇒ DB unreachable |
