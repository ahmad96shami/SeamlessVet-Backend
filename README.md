# VetSystem — Backend

Backend for the Integrated Veterinary Practice Management System — an Arabic-first platform for veterinary centers in the West Bank, Palestine that combine in-clinic care with field visits to farms and homes. One ASP.NET Core service hosts the REST API, the SignalR notifications hub, the Hangfire job server, and the offline-sync **write path** for both clients (Center Web App, Field Mobile App).

**Status:** Pre-build. Architecture, schema, and tech stack are locked (see docs below); implementation has not started. Update this line as slices land.

## Where things live

Authoritative product, schema, and tech docs sit one level up at the repo root. Backend-specific docs live next to this README as they are written.

| Doc | Owns |
|---|---|
| [`../docs/PRD.md`](../docs/PRD.md) | Product spec (two clients, roles, pets/medical, POS, inventory, contracts + farm batches, doctor entitlements + settlement, offline-first, RTL, MVP scope) |
| [`../docs/TECH_STACK.md`](../docs/TECH_STACK.md) | Backend + frontend tech choices, repo structure, PowerSync self-host setup, R2 pattern, Hangfire jobs |
| [`../docs/SCHEMA.md`](../docs/SCHEMA.md) | Every table, enum, index, FK — plus the server-enforced business invariants |

API surface, slice plans, and deeper backend docs will be added under `../docs/` and `docs/` as work begins (endpoint inventory, business-flow map, deployment, testing guide).

## Tech stack

- **.NET 10** ASP.NET Core Web API — **Minimal APIs** (route groups per resource, **not** controllers)
- **EF Core 10** + **Npgsql**
- **PostgreSQL 17** — **no PostGIS** (no geospatial needs); logical replication enabled for PowerSync
- **PowerSync** (self-hosted) for mobile offline sync — reads Postgres via logical replication; **all writes flow through this API**
- **Mapster** (source-generated) for object mapping — not AutoMapper
- **SignalR** for real-time notifications — single in-process hub, **no Redis backplane**
- **Hangfire** (Postgres storage) for background jobs (reminders, stock/expiry alerts, settlement notifications, scheduled reports)
- **FluentValidation** + **Serilog** + **NSwag** (OpenAPI)
- **`IMemoryCache`** for hot lookups + built-in ASP.NET Core rate limiting — **no Redis**
- **Cloudflare R2** (S3-compatible) for medical attachments (X-rays, lab PDFs) — private bucket, signed URLs only
- **JWT** + **BCrypt** for auth (email/phone + password; admin-approval registration)

## Repo layout

Mirrors `VetSystem.slnx`:

```
vet-backend/
├── src/
│   ├── VetSystem.Domain/          # entities, enums, domain rules — zero deps
│   ├── VetSystem.Application/     # services, validators, DTOs, repository interfaces, Mapster profiles
│   ├── VetSystem.Infrastructure/  # EF Core, Hangfire, R2, JWT, PowerSync token/JWKS
│   └── VetSystem.API/             # Minimal-API endpoint modules, SignalR hub, middleware, Program.cs
├── tests/
│   └── VetSystem.IntegrationTests/  # against a PostgreSQL test container (+ unit tests for money logic)
├── docker-compose.yml             # Postgres, PowerSync Service
├── .env                           # docker-compose env (POSTGRES_*, POWERSYNC_*)
└── docs/                          # backend-specific docs if any
```

## Quickstart

Prereqs: .NET 10 SDK, Docker, `dotnet ef` global tool (`dotnet tool install --global dotnet-ef`).

1. **Start Postgres + PowerSync** from the project root:

    ```bash
    docker compose up -d
    ```

    Reads `.env` for `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_PORT`, and the PowerSync Service settings. Postgres must run with `wal_level=logical` (set in the compose config) so PowerSync can stream changes.

2. **Configure secrets.** `appsettings.json` ships with development placeholders; override locally via `appsettings.Development.json` or `dotnet user-secrets` (UserSecretsId `vet-system-secrets`). At minimum set:

    - `ConnectionStrings:DefaultConnection` — Postgres
    - `Jwt:SecretKey` — ≥32 chars
    - `Storage:R2:*` — Cloudflare R2 credentials (User token, not Account token)
    - `PowerSync:*` — token-signing key / JWKS settings the PowerSync Service trusts

3. **Apply migrations** (auto-migrate is intentionally disabled — see `Program.cs`):

    ```bash
    dotnet ef database update \
      --project src/VetSystem.Infrastructure \
      --startup-project src/VetSystem.API
    ```

4. **Build and run, seeding test data on first start:**

    ```bash
    dotnet run --project src/VetSystem.API -- --seed
    ```

    Use `--force-seed` to wipe and re-seed. The API logs the listen URL on boot — open `/swagger` there. Other endpoints: `/health`, `/hangfire` (admin-gated), the SignalR hub at `/hubs/notifications`, and the sync write endpoints under `/sync/*`.

5. **Run tests:**

    ```bash
    dotnet test
    ```

## Dev seed accounts

Source of truth will be `src/VetSystem.Infrastructure/Persistence/DataSeeder.cs`. Define at least an approved **Admin** (registration is admin-gated, so the first admin must be seeded), plus sample **accountant**, **in-clinic vet**, **field doctor**, and **receptionist** accounts covering each role. Record the seeded logins here once `DataSeeder.cs` exists — do not ship real credentials.

## Contributing

Read the three locked docs above before non-trivial work: `SCHEMA.md` is the contract (and lists the invariants that must hold on every write), `PRD.md` wins on product behavior, `TECH_STACK.md` on implementation choices. The money-critical paths — doctor-entitlement calculations, exam-fee models, the settlement lock, and inventory delta math — require tests with any change. Branch off for each task; PRs target `sandbox`, not `main`.

## License

Proprietary. All rights reserved.
