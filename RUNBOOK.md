# VetSystem — Operations Runbook (M14)

Operating the production backend stack on a single VPS with Docker Compose. Companion files:
`docker-compose.prod.yaml`, `Dockerfile`, `.env.prod.example`, `secrets/README.md`,
`scripts/pg_dump_to_r2.sh`, `scripts/restore_from_r2.sh`, and `LAUNCH_CHECKLIST.md`.

> **Status note (read first).** The deploy / migrate / backup / restore paths below were validated by a
> full local bring-up of `docker-compose.prod.yaml`, and the PowerSync mobile read path now loads
> cleanly (sync rules were reworked JOIN-free — see [Known issues](#known-issues--pre-launch-blockers)).
> Sentry crash reporting (M13 task 8) is now wired — config-driven and OFF until you set `Sentry__Dsn`
> in `.env.prod` (see `.env.prod.example` and vet-backend/CLAUDE.md "Operations").

---

## Topology

```
                 Cloudflare (TLS edge, DDoS)
                          │  Full (Strict)
                          ▼
   ┌──────────────────  nginx  ──────────────────┐   (terminates Cloudflare Origin cert)
   │  vet.<domain>  → /srv/web  (static SPA)      │
   │  api.<domain>  → api:8080                    │
   │  sync.<domain> → powersync:8080              │
   └───────┬───────────────────────┬─────────────┘
           ▼                       ▼
         api  ──(EF migrate first)──►  postgres  ◄──logical replication──  powersync
   (Kestrel :8080, in-process            (private,                         (sync stream;
    Hangfire worker + SignalR)        not published)                    bucket storage on PG)
```

- **One VPS, one compose project** (`name: vet`). Postgres is **never published** to the host.
- **The Center Web App** is a static Vite PWA built by a one-shot `web-build` service from the
  sibling `../vet-frontend` repo into the `webdist` volume, which nginx serves at `vet.<domain>`.
  Both repos sit side-by-side on the VPS (e.g. `/opt/vet-backend` + `/opt/vet-frontend`). The API
  base URL is **compile-time** — baked in as `VITE_API_URL=https://api.<domain>` (subdomain split).
- **Hangfire runs in-process inside the `api` container** — per TECH_STACK, "Backend API + Hangfire" is
  one component (no Redis backplane). There is no separate worker container; see
  [Scale the Hangfire worker](#scale-the-hangfire-worker).
- **Migrations** are applied by a one-shot `migrate` service before the API boots (auto-migration is
  disabled in `Program.cs`).

---

## Canonical compose invocation

Secrets live in `.env.prod`, which compose needs both for `${...}` interpolation **and** as the API's
`env_file`. **Always pass `--env-file .env.prod`:**

```bash
cd /opt/vet-backend
alias dc='docker compose --env-file .env.prod -f docker-compose.prod.yaml'
dc ps          # etc.
```

The rest of this doc assumes that `dc` alias.

---

## Prerequisites

- A VPS with SSH + Docker Engine + the Compose plugin. Verify: `docker version`, `docker compose version`.
- **Both repos** checked out side-by-side: this one at `/opt/vet-backend` and the frontend at
  `/opt/vet-frontend` (the `web-build` service uses `../vet-frontend` as its build context). Adjust the
  `context:` in `docker-compose.prod.yaml` if your layout differs.
- A registered domain on Cloudflare (see [Cloudflare setup](#cloudflare-setup-dns--origin-certificate)).
- A Cloudflare R2 account: an **attachments** bucket and a **backups** bucket (see
  [R2 setup](#r2-setup-attachments--backups)).

---

## First-time deploy

1. **Secrets.** Create the two secret sources (details: `.env.prod.example`, `secrets/README.md`):

   ```bash
   cp .env.prod.example .env.prod && $EDITOR .env.prod          # PG password, JWT key, R2 creds, domain…
   # PowerSync RSA signing keypair → secrets/appsettings.Production.json  (see secrets/README.md §1)
   # Cloudflare Origin cert        → secrets/cloudflare-origin/{origin.pem,origin.key} (secrets/README.md §2)
   ```

   Generate strong values: `openssl rand -base64 48` for `Jwt__SecretKey`; a long random `PG_PASSWORD`.

2. **Build images:**

   ```bash
   dc build           # api (final), migrate (EF bundle), and web-build (../vet-frontend → static SPA)
   ```

   `web-build` bakes `VITE_API_URL=https://api.${DOMAIN}` into the bundle at build time, so it must be
   (re)built whenever `DOMAIN` changes or the frontend changes — `dc build web-build`.

3. **Bring it up.** Compose orders it for you (postgres healthy → migrate completes → api healthy →
   powersync → nginx):

   ```bash
   dc up -d --wait --wait-timeout 180
   ```

   On a **fresh** data volume, `db-init/00-publication.sql` creates the `powersync` publication
   automatically. The `migrate` service applies all EF migrations and exits 0; check it:

   ```bash
   dc logs migrate          # → "Applying migration '…'" … "Done.", exit 0
   ```

4. **Seed the bootstrap admin** (idempotent; first deploy only):

   ```bash
   dc run --rm api --seed
   ```

   Bootstrap admin = `+970000000000` / `BootstrapAdmin__Password`. **Log in and change it immediately.**

5. **Smoke-check** (see also `LAUNCH_CHECKLIST.md`):

   ```bash
   dc exec api curl -fsS http://localhost:8080/health/live      # {"status":"ok"}
   dc exec api curl -s   http://localhost:8080/health/ready      # see "ready" states below
   dc exec api curl -s   http://localhost:8080/.well-known/jwks.json | jq '.keys[].kid'
   ```

   From outside, through Cloudflare: `curl -fsS https://api.<domain>/health/live`, and the web app:
   `curl -fsS https://vet.<domain>/ | head` (should return the SPA `index.html`). Then open
   `https://vet.<domain>` in a browser and log in.

### `/health/ready` states (observed)

| Response | Meaning |
|---|---|
| `{"status":"ok","checks":{"database":"ok","hangfire":"ok","powersync":"ok"}}` | Fully healthy. |
| `…"powersync":"missing"…` → `"degraded"` | Replication slot not present yet — see [troubleshooting](#powersync--replication-slot). |
| HTTP 503 | DB unreachable. |

---

## Deploy WITHOUT a domain (sslip.io + Let's Encrypt)

For a VPS with only a public IP and no registered domain / no Cloudflare. Uses **sslip.io** (a free
wildcard-DNS service: `anything.<ip>.sslip.io` resolves to `<ip>`) for real hostnames, and **Let's
Encrypt** (direct, via the `docker-compose.letsencrypt.yaml` overlay) for browser-trusted TLS — so the
offline PWA, subdomain routing, and everything else work exactly like the Cloudflare deploy.

Prereqs: ports **80 and 443** open to the internet (HTTP-01 needs 80). On Hetzner, open both in the
Cloud Firewall **and** the host `ufw`.

1. **Config** — in `.env.prod` set the IP-based hostname + an email for expiry notices:

   ```bash
   DOMAIN=203.0.113.45.sslip.io        # ← your VPS public IP, dots kept, + .sslip.io
   LETSENCRYPT_EMAIL=you@example.com
   ```

   (Everything else in `.env.prod` / `secrets/appsettings.Production.json` is the same as the normal
   deploy. You do **not** need `secrets/cloudflare-origin/`.)

2. **Issue the cert + bring up the stack** (one command — builds images, makes a temporary self-signed
   cert so nginx can boot, serves the HTTP-01 challenge, gets the real cert, reloads):

   ```bash
   ./scripts/init-letsencrypt.sh --staging     # FIRST run: staging cert (avoids LE rate limits)
   ./scripts/init-letsencrypt.sh               # then the real, browser-trusted cert
   ```

3. **Seed + verify** (same as the normal deploy, but with the `dcle` alias):

   ```bash
   alias dcle='docker compose --env-file .env.prod -f docker-compose.prod.yaml -f docker-compose.letsencrypt.yaml'
   dcle run --rm api --seed
   curl -fsS https://api.$DOMAIN/health/live      # {"status":"ok"}  (real cert ⇒ no -k needed)
   ```
   Open `https://vet.<ip>.sslip.io` and log in.

**Renewal is automatic**: the `certbot` service runs `certbot renew` every 12h and nginx reloads every
6h. **Use `dcle` (both compose files) for all ops** in this mode — `dc` alone would start nginx in
Cloudflare-cert mode and fail.

**Moving to a real domain later**: change `DOMAIN` in `.env.prod`, `dcle build web-build` (the API URL is
baked into the SPA), then either re-run `init-letsencrypt.sh` (keep direct TLS) or switch to Cloudflare
(drop the overlay, add `secrets/cloudflare-origin/`, use plain `dc` — see [Cloudflare setup](#cloudflare-setup-dns--origin-certificate)).

---

## Redeploy / upgrade

```bash
cd /opt/vet-backend && git pull
cd /opt/vet-frontend && git pull        # the web app is built from here
cd /opt/vet-backend
dc build
dc up -d --wait --wait-timeout 180     # migrate re-runs (no-op if no new migrations), then api restarts
```

`migrate` is safe to re-run — EF skips already-applied migrations. `web-build` re-runs too, refreshing
the `webdist` volume with the new bundle before nginx comes back. `restart: unless-stopped` keeps the
long-running services up across reboots (the two one-shots, `migrate` and `web-build`, stay `exited 0`).

To redeploy **only the web app** (frontend change, no backend change):

```bash
cd /opt/vet-frontend && git pull && cd /opt/vet-backend
dc build web-build && dc up -d web-build && dc exec nginx nginx -s reload
```

---

## Database migrations

Applied by the one-shot **`migrate`** service, which runs an EF Core *migrations bundle* (a self-applying
executable baked into the `migrate` image — no SDK or source needed on the VPS). The API's `depends_on`
gates its own start on `migrate` completing successfully.

- Run only migrations: `dc up migrate` (foreground; shows applied migrations, exits 0).
- Manual fallback (from a dev machine with the SDK):
  ```bash
  dotnet ef database update --project src/VetSystem.Infrastructure --startup-project src/VetSystem.API
  ```

---

## Backups

Daily logical `pg_dump` → Cloudflare R2 (off-host), with 30-day retention.

1. **Config:** `cp secrets/backup.env.example secrets/backup.env && $EDITOR secrets/backup.env`
   (scoped R2 token — Object Read & Write on the **backups** bucket only; kept out of `.env.prod` so it
   never enters the API container).

2. **Schedule it** (the stack must be running so the script can `pg_dump` from the container):

   ```bash
   sudo cp scripts/crontab.example /etc/cron.d/vet-backup   # or `crontab -e` and paste the line
   ./scripts/pg_dump_to_r2.sh        # run once by hand to confirm it uploads
   ```

3. **30-day retention** is enforced by an **R2 lifecycle rule**, not the script. Set it in the Cloudflare
   dashboard → R2 → the bucket → Settings → Object lifecycle rules: *delete objects older than 30 days*
   (scope it to the backups prefix, e.g. `backups/pg/`). Note: a least-privilege **Object Read/Write**
   backup token **cannot** set this via the S3 API (`PutBucketLifecycleConfiguration` → `AccessDenied`,
   confirmed against R2) — use the dashboard, or a one-off admin token.

> **Validated against real R2 (2026-05-24):** with the supplied Object-R/W token, `pg_dump_to_r2.sh`
> uploaded to `seamlessvet/backups/pg/` and `restore_from_r2.sh --latest` pulled it back for a full
> restore→resume (~13 s, local stack). The token is correctly scoped (no bucket-admin), so lifecycle is
> a dashboard step. **Recommended for production:** a **dedicated backups bucket** with its own scoped
> token, separate from the attachments bucket, so the backup credential can't read medical attachments.

Backups are **plain SQL, gzipped** (`vet-YYYYMMDD-HHMMSS.sql.gz`) — restore with `psql`, no tooling
version pinning needed.

---

## Restore from backup

Use `scripts/restore_from_r2.sh` (reads `secrets/backup.env`). It downloads an object and restores it
into a target DB, **dropping and recreating** that DB — hence `--yes` is required.

```bash
# Drill into a scratch DB (safe; doesn't touch 'vet'):
./scripts/restore_from_r2.sh --latest --target-db vet_restore_test --yes

# Real recovery into the live DB (stop the API first so nothing writes mid-restore):
dc stop api powersync
./scripts/restore_from_r2.sh --key pg/vet-20260524-031500.sql.gz --target-db vet --yes
dc up -d --wait
```

On restore the schema + data come back intact; **PowerSync re-attaches its replication slot
automatically** on next start (the publication is restored with the dump; the slot is recreated by the
service). No manual surgery required.

### Restore-drill result

A full disaster-recovery drill was run locally against the prod compose stack, swapping R2 for a MinIO
(S3-compatible) container so the **actual** `pg_dump_to_r2.sh` / `restore_from_r2.sh` were exercised:
`pg_dump_to_r2.sh` → object in the bucket → `docker compose down -v` (total loss) → fresh Postgres →
`restore_from_r2.sh --latest` → `up api powersync`. Result: data intact, the `powersync` publication
came back **with the dump** (no manual recreate), PowerSync recreated its slot and `/health/ready`
returned `powersync: ok` — i.e. it resumed without manual surgery. Restore + bring-up: **~12 s** (small
seeded dataset).

On the VPS, re-run against the **real R2 bucket** and a representative dataset, and record it here as
part of launch sign-off (target: clean VPS → working state under 1 hour — `LAUNCH_CHECKLIST.md`):

| Date | Store | Dataset | Restore+resume time | By |
|---|---|---|---|---|
| 2026-05-24 | MinIO (R2 stand-in, local) | seed only | ~12 s | dev (mechanism check) |
| 2026-05-24 | **real R2** (`seamlessvet`), local stack | seed only | ~13 s | dev (real-R2 round-trip) |
| _(record at launch)_ | real R2, on the VPS | representative | | |

---

## Rotate secrets

Secrets are read at process start, so rotation = update the source, then restart the API.

| Secret | Where | After changing |
|---|---|---|
| `Jwt__SecretKey` | `.env.prod` | `dc up -d api` — **invalidates all access tokens** (users re-login; refresh tokens are server-side). |
| `PG_PASSWORD` | `.env.prod` **and** Postgres role | `ALTER ROLE vet PASSWORD …` in the DB, update `.env.prod`, then `dc up -d` (restarts api + powersync with the new URI). |
| `R2__*` | `.env.prod` | `dc up -d api`. |
| PowerSync RSA key | `secrets/appsettings.Production.json` | `dc up -d api` — **rotating the key invalidates outstanding PowerSync client tokens**; mobile clients re-fetch on next `/auth/powersync-token`. Keep the `Kid` in sync. |
| Backup R2 token | `secrets/backup.env` | none (used only by the backup/restore scripts). |

Never commit real values: `.env.prod` and `secrets/*` are gitignored.

---

## Scale the Hangfire worker

Hangfire runs **in-process** in the `api` container with a single worker by default. To process jobs
faster, raise the worker count (no code change):

```bash
echo 'Hangfire__WorkerCount=2' >> .env.prod   # keep modest on a single VPS
dc up -d api
```

Do **not** run a second `api` replica to "scale jobs": without a Redis backplane each instance starts
its own Hangfire server and re-registers the recurring jobs. One API instance with a higher worker count
is the supported path on this single-VPS topology.

---

## Observability

The **alert thresholds + saved Seq queries are the canonical reference in `vet-backend/CLAUDE.md` →
Operations → "Dashboards & alert thresholds"** — keep this section a pointer, not a copy. Four surfaces:

- **Sentry (crash/error reporting).** Wired but **OFF until you set a DSN**. Add `Sentry__Dsn` to
  `.env.prod` (Sentry project → Settings → Client Keys → DSN), then `dc up -d api`; set `Sentry__Release`
  to the deployed git SHA for per-deploy issue grouping. Captures HTTP 500s + Hangfire job failures with
  stacks; PII and request bodies are never sent. Alert on new/spiking/regressed issues (see CLAUDE.md).
- **Seq (structured logs).** Set `Seq__ServerUrl` to your Seq instance (else file-only at `/app/logs` in
  the `api-logs` volume). Key queries: `@Level='Error'`, `StatusCode=429` (rate-limited syncs),
  `Elapsed>1000` (slow requests vs. the `loadtests/` baseline).
- **Hangfire dashboard.** `https://api.<domain>/hangfire` (Admin-gated). Watch the **Failed** tab and
  that the daily jobs (07:00 UTC) + weekly report (Mon) keep succeeding.
- **Uptime monitor.** Point it at `/health/ready` (not `/live`): HTTP `503` ⇒ DB down (**page**);
  `status:"degraded"` ⇒ Hangfire or the PowerSync slot is down.

---

## Cloudflare setup (DNS + Origin Certificate)

1. **DNS:** add proxied (orange-cloud) records for `vet.<domain>`, `api.<domain>`, and `sync.<domain>`
   → the VPS IP (optionally the apex `<domain>` too — nginx 301s it to `vet.<domain>`).
2. **Origin Certificate:** SSL/TLS → Origin Server → Create Certificate. Cover `<domain>` **and**
   `*.<domain>`. Save the cert + key to `secrets/cloudflare-origin/origin.pem` / `origin.key`
   (`chmod 600` the key). See `secrets/README.md` §2.
3. **SSL/TLS mode:** set to **Full (Strict)** so Cloudflare validates the origin cert.
4. Optional: restore real client IPs in nginx by uncommenting the `set_real_ip_from` block in
   `nginx/conf.d/vet.conf.template` with the current [Cloudflare IP ranges](https://www.cloudflare.com/ips/).

Verify TLS termination: `curl -fsS https://api.<domain>/health/live` returns `{"status":"ok"}`.

---

## R2 setup (attachments + backups)

- **Attachments bucket** (private; signed URLs only): create it, then an R2 **user** API token (Object
  Read & Write). Put the endpoint/keys/bucket into `.env.prod` (`R2__*`).
- **Backups bucket** (separate): create it + a **scoped** token (Object Read & Write on this bucket
  only). Put into `secrets/backup.env`. Add the 30-day lifecycle rule (see [Backups](#backups)).

---

## VPS snapshots (weekly)

Single-VPS hosting is the biggest infra risk, so layer a **weekly full snapshot** on top of the daily
R2 dumps. If the provider exposes snapshots via API/panel, schedule weekly; if manual, do it weekly and
note the procedure here:

```
Provider: __________   Snapshot cadence: weekly   How: __________   Retention: __________
```

---

## Staging environment

Mirror production on a smaller VPS: same `docker-compose.prod.yaml`, a **separate** `.env.prod`
(separate `DOMAIN`, e.g. `*.staging.<domain>`), a **separate R2 bucket**, and separate Cloudflare
hostnames. Mobile: EAS internal-distribution builds point at `sync.staging.<domain>`. Dry-run
`LAUNCH_CHECKLIST.md` here before each production release.

---

## Troubleshooting

### PowerSync — replication slot

After `powersync` starts, confirm it attached its slot:

```bash
dc exec -T postgres psql -U vet -d vet -c \
  "SELECT slot_name, plugin, slot_type, active FROM pg_replication_slots;"
```

Expected: one `pgoutput` logical slot, `active = t`. Healthy PowerSync boot logs (v1.10.2):

```
Collecting PowerSync configuration from File: /config/config.yaml
Successfully registered Replicator postgresql-vet with ReplicationEngine
Successfully started Storage Engine.  /  Router Engine.  /  Replication Engine.
Service started
```

If the slot is **missing**, the usual causes (in order seen during bring-up):

| Symptom in `dc logs powersync` | Cause | Fix |
|---|---|---|
| `Config file path /app/onfig … does not exist` | `-config` flag parsed as `-c onfig` by the runner's arg parser. | Use `POWERSYNC_CONFIG_PATH` env (already set in compose), not `-config`. |
| `Fatal error SSL required but not supported` (`PgError.nossl`) | Driver wants TLS; internal PG has none. | `sslmode: disable` on **both** the replication connection and `storage` in `powersync/config.yaml` (already set). The driver ignores `?sslmode` in the URI. |
| `Must SELECT from a single table` | a `sync-rules.yaml` data query uses a JOIN. | Rules are JOIN-free as of M14 (parameter buckets + denormalized scope keys). If it recurs, a new query reintroduced a JOIN — re-express it via a bucket parameter. |
| publication missing | data volume pre-existed, so `db-init` didn't run | `dc exec -T postgres psql -U vet -d vet -c "CREATE PUBLICATION powersync FOR ALL TABLES;"` then restart powersync. |
| `client_auth.jwks_uri` unreachable | API not healthy yet | ensure `api` is healthy (`dc ps`); powersync reaches it at `http://api:8080/.well-known/jwks.json` on the compose network. |

### Logs & health

- `dc logs -f api` — Serilog also rolls files into the `api-logs` volume (`/app/logs`).
- `dc exec api curl -s http://localhost:8080/health/ready` — `degraded` ⇒ Hangfire or the PowerSync slot
  is down; `503` ⇒ DB unreachable.
- Hangfire dashboard: `https://api.<domain>/hangfire` (Admin-gated).

---

## Known issues & pre-launch blockers

1. **PowerSync sync-rules JOINs — RESOLVED (M14).** `powersync/sync-rules.yaml` originally scoped child
   tables with JOINs, which PowerSync rejects (`Must SELECT from a single table`). It was reworked into
   **parameter-query buckets** (`doctor`, `by_customer`, `by_visit`, `by_field_inventory`, `by_contract`),
   and the three append-only tables 2+ levels deep (`ledger_entries`, `invoice_items`, `payments`) plus
   `vaccinations` carry a **denormalized scope key** kept correct by BEFORE INSERT/UPDATE triggers
   (migration `M14_SyncScopeDenormalization`). Validated: the rules load cleanly, the slot is active, and
   the trigger/scoping behavior is covered by the test suite. Keep new data queries single-table.
2. **Sentry — wired, DSN not set (M13 task 8).** The SDK is wired (environment + release tagging; logged
   errors and Hangfire job failures are captured, 5xx request-log duplicates excluded). It is
   **config-driven and OFF until `Sentry__Dsn` is set** in `.env.prod` (see `.env.prod.example`). Set it
   before launch for crash/error alerting.
