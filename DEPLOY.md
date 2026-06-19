# DEPLOY.md — Deploying SeamlessVet, explained from scratch

This is the **"start here"** deployment guide, written for someone new to servers and Docker. It
describes the system we actually deployed (a single Hetzner VPS, no domain, free TLS), what every
moving part is and why it exists, and — the part you asked for — **exactly what to do when you've
been coding locally and want to push a new version live.**

For deep operational detail (backups, secret rotation, every troubleshooting case) see the companion
**`RUNBOOK.md`**. For the pre-go-live sign-off, see **`LAUNCH_CHECKLIST.md`**. This file is the
narrative that ties them together.

> **One thing to internalize first:** the deployment is *driven from this repo* (`vet-backend`). Every
> command in this guide is run from `/opt/vet-backend` on the server. The web app lives in the sibling
> `vet-frontend` repo, but it is **built by** a service defined here. So "deploy" always starts here.

---

## 0. Quick reference card

Your live system, right now:

| Thing | Value |
|---|---|
| VPS | Hetzner, `46.225.103.49`, Ubuntu 24.04 (2 vCPU / 4 GB RAM + 2 GB swap) |
| SSH in | `ssh root@46.225.103.49` (key `~/.ssh/id_ed25519`) |
| Base hostname (`DOMAIN`) | `46.225.103.49.sslip.io` (a free DNS trick — see §1.4) |
| Web app | https://vet.46.225.103.49.sslip.io |
| API | https://api.46.225.103.49.sslip.io |
| Mobile sync stream | https://sync.46.225.103.49.sslip.io |
| TLS | Let's Encrypt (real, browser-trusted), auto-renewing |
| Object storage | Cloudflare R2 bucket `seamlessvet` (attachments) |
| Code on server | `/opt/vet-backend` + `/opt/vet-frontend` |
| Secrets on server | `/opt/vet-backend/.env.prod` + `/opt/vet-backend/secrets/` (never in git) |

**The alias you'll type all day** (run it once each SSH session, from `/opt/vet-backend`):

```bash
cd /opt/vet-backend
alias dcle='docker compose --env-file .env.prod -f docker-compose.prod.yaml -f docker-compose.letsencrypt.yaml'
```

> ⚠️ **Always use `dcle` (both `-f` files), never plain `docker compose`.** Your TLS comes from the
> Let's Encrypt *overlay* (`docker-compose.letsencrypt.yaml`). Leave it off and nginx boots expecting a
> Cloudflare certificate you don't have, and the site goes down. The RUNBOOK calls the Cloudflare-mode
> alias `dc`; **you are not on that path — you use `dcle`.**

**The three commands you'll use most:**

```bash
dcle ps                       # what's running
dcle logs -f api              # follow the API logs (Ctrl-C to stop)
dcle up -d --wait             # bring everything to its desired state
```

**Accounts** (passwords are NOT in this file — they live in `.env.prod`; see §3):

| Login | Phone | Where the password lives | Used for |
|---|---|---|---|
| Bootstrap admin | `+970000000000` | `.env.prod` → `BootstrapAdmin__Password` | the seeded center's first admin |
| Platform owner | `+970500000000` | `.env.prod` → `PlatformAdmin__Password` | the super-admin console at `/platform/login` |

---

## 1. What we deployed, and why — the mental model

### 1.1 The two repos

```
SeamlessVet/
├── vet-backend/    ← .NET 10 API + ALL the deployment files (compose, nginx, scripts). YOU DEPLOY FROM HERE.
└── vet-frontend/   ← the pnpm workspace; apps/web is the Center Web App. Built BY a service in vet-backend.
```

They are independent repos but on the server they sit **side by side** (`/opt/vet-backend` and
`/opt/vet-frontend`) because the backend's build process reaches "next door" (`../vet-frontend`) to
compile the web app.

### 1.2 Why containers / Docker Compose

Instead of installing .NET, PostgreSQL, nginx, etc. directly on the VPS (fragile, version-conflict
prone), each piece runs in its own **container** — a sealed box with exactly the software it needs. A
**Docker image** is the recipe; a **container** is a running instance of it. **Docker Compose** is the
tool that starts all the containers together, wires their network, and keeps them running. Our whole
stack is described in two files:

- **`docker-compose.prod.yaml`** — the base stack (all services).
- **`docker-compose.letsencrypt.yaml`** — a small *overlay* that adds free TLS (your no-domain path).

Compose merges them when you pass both with `-f` (that's what `dcle` does).

### 1.3 The services (what each container is)

When you run `dcle ps` you'll see these. Two are **one-shot** (they do a job and exit `0`); the rest
stay running (`restart: unless-stopped` brings them back after a reboot or crash).

| Service | Type | What it does |
|---|---|---|
| `postgres` | long-running | PostgreSQL 17 — the database. **Never exposed to the internet**; only the other containers can reach it. Logical replication is enabled so PowerSync can tail changes. |
| `migrate` | one-shot | Applies database schema migrations (EF Core), then exits. Runs **before** the API starts. Re-running it is safe — already-applied migrations are skipped. |
| `api` | long-running | The .NET API (Kestrel on port 8080). Also runs Hangfire (background jobs: reminders, alerts, monthly salary accrual) and SignalR (live notifications) **in-process** — no separate worker. |
| `powersync` | long-running | The PowerSync service. Streams DB changes to the **mobile** app so field doctors work offline. Reads Postgres via replication; trusts the API to mint its tokens. |
| `web-build` | one-shot | Compiles `../vet-frontend`'s web app into static files and drops them into a shared volume, then exits. nginx waits for this before serving the site. |
| `nginx` | long-running | The **reverse proxy / TLS edge**. The only thing exposed to the internet (ports 80 + 443). Terminates HTTPS and routes by subdomain (see below). Serves the static web app directly. |
| `certbot` | long-running | (Overlay only.) Gets and auto-renews the Let's Encrypt certificate. |

**Routing** is by subdomain so paths never collide (the API has its own `/sync/...` write endpoints,
separate from PowerSync's sync stream):

```
              the internet
                   │  HTTPS
                   ▼
   ┌─────────────  nginx  ─────────────┐
   │  vet.<DOMAIN>  → static web app    │  (files served straight from disk)
   │  api.<DOMAIN>  → api:8080          │  (REST + SignalR websocket + sync write path)
   │  sync.<DOMAIN> → powersync:8080    │  (mobile sync stream, long-lived websocket)
   └──────┬──────────────────┬──────────┘
          ▼                  ▼
        api ──migrate first──► postgres ◄──replication── powersync
```

### 1.4 The "no domain" trick: sslip.io + Let's Encrypt

You don't own a domain, but HTTPS certificates need a *hostname*, not an IP. Two free pieces solve it:

- **sslip.io** is a public DNS service: any hostname like `46.225.103.49.sslip.io` (or
  `vet.46.225.103.49.sslip.io`) automatically resolves to the IP embedded in it — `46.225.103.49`. No
  registration, no cost. That's why `DOMAIN=46.225.103.49.sslip.io` in your config.
- **Let's Encrypt** issues free, browser-trusted certificates. Our `certbot` container proves we control
  the hostname via the **HTTP-01 challenge** (Let's Encrypt asks for a file at
  `http://vet.<DOMAIN>/.well-known/acme-challenge/...`; nginx serves it), then hands us a real cert.

The result is identical to a paid domain: a green padlock, working PWA, working subdomain routing.

> If you ever buy a real domain, you switch by changing `DOMAIN` in `.env.prod`, rebuilding the web app,
> and re-issuing the cert (or moving to Cloudflare). See RUNBOOK → "Moving to a real domain later".

### 1.5 Where everything physically lives on the server

```
/opt/vet-backend/                 ← this repo (compose files, nginx config, scripts) — rsynced from your laptop
  ├── .env.prod                   ← all scalar secrets (gitignored, server-only)   ← NEVER overwrite blindly
  ├── secrets/
  │    └── appsettings.Production.json   ← the PowerSync signing key (multi-line, can't live in .env)
  └── docker-compose*.yaml, nginx/, scripts/, db-init/, Dockerfile
/opt/vet-frontend/                ← the frontend repo (source the web app is built from) — also rsynced

Docker-managed (not files you edit) — survive restarts, live in /var/lib/docker/volumes:
  pgdata        ← the actual database contents
  webdist       ← the compiled web app nginx serves
  certbot-etc   ← your TLS certificate + private key
  api-logs      ← rolling Serilog log files
```

---

## 2. The live deployment — what we actually did (history)

For context, here's the sequence that got you to a working system. You don't repeat all of this on a
redeploy (that's §5) — this is the **first-time** path, done once:

1. **Provisioned the VPS** (Hetzner, Ubuntu 24.04) and installed Docker + the Compose plugin.
2. **Opened the firewall** for ports 80 and 443 (HTTP-01 needs 80) — in both the Hetzner Cloud Firewall
   and the host's `ufw`.
3. **Copied the code** to `/opt/vet-backend` and `/opt/vet-frontend` (via `rsync` — see §5).
4. **Created the secrets on the server** (never committed):
   - `.env.prod` — DB password, JWT key, R2 credentials, `DOMAIN=46.225.103.49.sslip.io`,
     `LETSENCRYPT_EMAIL`, bootstrap admin password.
   - `secrets/appsettings.Production.json` — the PowerSync RSA signing keypair.
5. **Issued TLS + brought the stack up** with one script:
   ```bash
   ./scripts/init-letsencrypt.sh --staging   # first, with staging certs (avoids rate limits while testing)
   ./scripts/init-letsencrypt.sh             # then for real, browser-trusted cert
   ```
   This builds all images, makes a temporary self-signed cert so nginx can boot, serves the challenge,
   gets the real cert, and reloads. **You only run this once** — renewal is automatic afterward.
6. **Seeded** the bootstrap admin and center: `dcle run --rm -T api --seed`.
7. **Wired R2** — put the real Cloudflare R2 credentials into `.env.prod` and recreated the API; verified
   attachments upload/download.
8. **Enabled the platform super-admin console** — set `PlatformAdmin__Password` in `.env.prod` and ran a
   seed so the `/platform/login` console works (see §6.4).

Everything was smoke-tested over the public internet (login, health, TLS, sync). The detailed
fixes/gotchas from that bring-up are recorded in `RUNBOOK.md` and the project memory.

---

## 3. Secrets — what they are and the one rule

**The one rule: secrets live ONLY on the server and are NEVER committed to git.** `.env.prod` and
everything under `secrets/` are gitignored. The repo only ships `*.example` templates.

There are two secret *files* on the server:

**`/opt/vet-backend/.env.prod`** — line-based `KEY=value`. Compose uses it two ways: to fill in
`${...}` placeholders in the compose files, and to inject every `Section__Key` line as a configuration
value into the API container (the `__` becomes a `:` in .NET config). The important keys:

| Key | What it is |
|---|---|
| `PG_PASSWORD` | PostgreSQL password (reused to build the API + PowerSync connection strings). |
| `DOMAIN` | `46.225.103.49.sslip.io`. Also **baked into the web app** at build time (see §4.2). |
| `LETSENCRYPT_EMAIL` | Where Let's Encrypt sends expiry notices. |
| `Jwt__SecretKey` | Signs user login tokens (≥ 32 chars). |
| `R2__ServiceUrl` / `R2__AccessKey` / `R2__SecretKey` / `R2__Bucket` | Cloudflare R2 attachment storage. |
| `BootstrapAdmin__Password` | First admin of the seeded center (`+970000000000`). |
| `PlatformAdmin__Password` | The platform super-admin (`+970500000000`) for `/platform/login`. |
| `POWERSYNC_IMAGE_TAG` | Pinned to `1.21.0` (the sync-rules format requires ≥ 1.21.0). |

**`/opt/vet-backend/secrets/appsettings.Production.json`** — holds the one secret that can't live in a
`.env` file: the PowerSync RSA signing keypair (multi-line PEM). It's mounted read-only into the API.

To generate fresh strong values: `openssl rand -base64 48`.

> Because secrets live only on the server, **your redeploy must never overwrite them** — §5 excludes
> them from the file copy. If you ever lose `.env.prod`, you lose the DB password etc.; keep a copy in a
> password manager.

---

## 4. Continuing development locally

### 4.1 Running locally

You already do this. Nothing about deploying changes your local loop:

- **Backend:** `dotnet run --project src/VetSystem.API` → API at `http://localhost:5180` (Swagger at
  `/swagger`). For the full local stack incl. PowerSync, the dev compose file in `vet-backend`.
- **Web:** in `vet-frontend`, `pnpm --filter @vet/web dev` → Vite dev server (talks to `localhost:5180`).
- **Mobile:** Expo, as usual.

Build/test before you consider a change shippable: `dotnet build` + `dotnet test` (backend), `pnpm
typecheck` / `pnpm build` (web). The CLAUDE.md files in each project have the specifics.

### 4.2 The one gotcha that surprises people: the web app's API URL is baked in at build time

Vite compiles the web app into static files, and the API base URL (`VITE_API_URL`) is **frozen into
that bundle at build time** — it is not read at runtime. So:

- Locally, the bundle points at `http://localhost:5180`.
- On the server, the `web-build` service rebuilds it with `VITE_API_URL=https://api.46.225.103.49.sslip.io`.

**Consequence:** you can't just copy a locally-built `dist/` to the server — it would point at
`localhost`. The server **rebuilds the web app itself** (that's what `web-build` is for). You just ship
the *source*. This also means: **if you ever change `DOMAIN`, you must rebuild the web app** (`dcle
build web-build`), or it'll keep calling the old API host.

---

## 5. Redeploy: pushing a local change live  ⭐ (the part you asked about)

You changed code locally, it builds and tests clean, now you want it live. Here's the whole flow.

### Step 0 — save your work locally

Commit on `main` (solo-dev workflow — no branches). Committing isn't *required* for the rsync method
below (it copies your working tree, committed or not), but it's good hygiene and gives you a point to
roll back to.

### Step 1 — ship the code to the server

Your server's `/opt` directories were populated by **rsync** (you don't have a git remote configured
yet), so that's the redeploy method. Run these **from your laptop**, from `~/projects/SeamlessVet`:

```bash
# Backend → /opt/vet-backend.  Excludes protect server-only secrets and skip build junk.
rsync -az --delete \
  --exclude '.git' --exclude 'bin/' --exclude 'obj/' \
  --exclude '.env.prod' --exclude 'secrets/' \
  vet-backend/ root@46.225.103.49:/opt/vet-backend/

# Frontend → /opt/vet-frontend.  node_modules/dist are rebuilt inside Docker, so don't ship them.
rsync -az --delete \
  --exclude '.git' --exclude 'node_modules' --exclude 'dist' --exclude '.expo' \
  vet-frontend/ root@46.225.103.49:/opt/vet-frontend/
```

> **Why the `--exclude '.env.prod'` and `--exclude 'secrets/'` matter:** those files exist only on the
> server. With rsync, an excluded file is also **protected from `--delete`**, so these excludes mean the
> copy will neither overwrite nor delete your server secrets. Never drop these two excludes.

> **Tip — once you set up a GitHub (or similar) remote**, the cleaner method is `git pull` on the server
> instead of rsync (see RUNBOOK → "Redeploy / upgrade"). Same idea: get the new source onto the box.

### Step 2 — rebuild images and bring the stack to its new state

SSH in and run (remember the `dcle` alias from §0):

```bash
ssh root@46.225.103.49
cd /opt/vet-backend
alias dcle='docker compose --env-file .env.prod -f docker-compose.prod.yaml -f docker-compose.letsencrypt.yaml'

dcle build                       # rebuild api, migrate, and web-build images from the new source
dcle up -d --wait --wait-timeout 240
```

`dcle up` figures out everything for you in the right order: Postgres healthy → `migrate` applies any
new migrations and exits → `api` starts (only after migrate succeeds) → `powersync` → `web-build`
republishes the site → `nginx` comes back. Containers whose images didn't change aren't disturbed.

### Step 2b — faster path when ONLY the web app changed

If you touched only `vet-frontend` (no backend, no DB change), you don't need to rebuild the API:

```bash
dcle build web-build
dcle up -d web-build              # recompiles the site into the webdist volume
dcle exec nginx nginx -s reload  # nginx serves the fresh files
```

### Step 3 — database migrations (automatic)

If your change added EF migrations, the `migrate` one-shot applies them during `dcle up` — you don't run
anything by hand. Confirm:

```bash
dcle logs migrate                # ends with the migrations applied and exit 0
```

(There's a manual fallback from a dev machine with the .NET SDK; see RUNBOOK → "Database migrations".)

### Step 4 — verify it's live

```bash
# On the server (inside the api container):
dcle exec api curl -s http://localhost:8080/health/ready
#   → {"status":"ok","checks":{"database":"ok","hangfire":"ok","powersync":"ok"}}

# From anywhere (real cert, no -k needed):
curl -fsS https://api.46.225.103.49.sslip.io/health/live      # {"status":"ok"}
curl -fsS https://vet.46.225.103.49.sslip.io/ | head          # the SPA's index.html
```

Then open https://vet.46.225.103.49.sslip.io in a browser and click around. Done.

### What you must NOT touch on a redeploy

- **Don't re-run `init-letsencrypt.sh`.** It's a *first-issuance* script. The cert auto-renews; re-running
  it risks Let's Encrypt rate limits. (`dcle up` does not disturb the existing cert.)
- **Don't overwrite `.env.prod` or `secrets/`** (the rsync excludes handle this).
- **Don't `--force-seed` or `down -v`** unless you actually want to wipe data — see §6.3.

---

## 6. Day-2 operations

All commands assume you've SSH'd in, are in `/opt/vet-backend`, and have set the `dcle` alias.

### 6.1 Status, logs, restart

```bash
dcle ps                          # service states; the two one-shots show "exited (0)" — that's normal
dcle logs -f api                 # follow API logs (also rolled to files in the api-logs volume)
dcle logs powersync              # sync service logs
dcle restart api                 # restart just the API (e.g. after editing .env.prod)
dcle up -d                       # reconcile everything to desired state (safe to run anytime)
dcle down                        # stop & remove containers (DATA SURVIVES — volumes are kept)
```

### 6.2 Changed a secret / setting in `.env.prod`?

The API reads config at startup, so: edit `.env.prod`, then `dcle up -d api` (recreates the API with the
new values). For example, after adding a Sentry DSN or bumping the Hangfire worker count. Some changes
have side effects (rotating `Jwt__SecretKey` logs everyone out) — see RUNBOOK → "Rotate secrets".

### 6.3 Seeding and cleaning the database  ⚠️

Seeding runs the **api** image with a flag (the database isn't seeded automatically):

```bash
dcle run --rm -T api --seed         # IDEMPOTENT & additive: creates anything missing, touches nothing existing
dcle run --rm -T api --force-seed   # ⚠️ DESTRUCTIVE: wipes ALL tenant data, then re-seeds from scratch
```

> The `-T` is important when running over SSH — without it `docker compose run` can swallow the rest of
> a piped command.

**What `--force-seed` does:** it `TRUNCATE`s all tenant tables (centers, users, customers, visits,
invoices, inventory, ledgers, …) and re-creates the **Bootstrap** center + its admin (`+970000000000`)
+ the platform admin. It **does not** touch `platform_admins` (that table is preserved). Use it to reset
to a clean slate. Everyone is logged out (re-login required).

**Total wipe** (including the DB volume — also resets PowerSync's replication slot):

```bash
dcle down -v                     # ⚠️ DELETES the pgdata volume — the database is GONE
dcle up -d --wait                # fresh empty DB; on first boot the powersync publication is recreated
dcle run --rm -T api --seed
```

There is **no "delete a tenant"** anywhere — the platform console only **suspends/reactivates**. So the
clean way to end up with just your real center is: clean → provision your center (§6.4) → suspend the
Bootstrap one.

### 6.4 The platform super-admin console + provisioning a tenant (center)

The platform console is a separate super-admin login used to create/suspend tenants (centers):

- URL: **https://vet.46.225.103.49.sslip.io/platform/login**
- Login: `+970500000000` / the value of `PlatformAdmin__Password` in `.env.prod`.

It was enabled by setting `PlatformAdmin__Password` in `.env.prod` and running a seed (the platform
admin row is only created during a seed). From the console you can list tenants, **provision a new
center** (center name, code, mode = `solo` or `partnership`, and that center's first admin's
name/phone/password/email), and suspend/reactivate.

> If `/platform/login` ever rejects valid credentials, the row probably isn't seeded — confirm with
> `dcle exec -T postgres psql -U vet -d vet -c "SELECT phone, full_name FROM platform_admins;"`, then
> ensure `PlatformAdmin__Password` is set and run `dcle run --rm -T api --seed`.

### 6.5 Backups (set this up before real data exists)

Daily `pg_dump` → a **separate** Cloudflare R2 bucket, 30-day retention. This is **not yet scheduled** on
your box. The full procedure (scoped backup token, cron entry, lifecycle rule, and a tested restore
script) is in RUNBOOK → "Backups" and "Restore from backup". Do this before you onboard a real center.

### 6.6 TLS certificate — nothing to do

The `certbot` container renews automatically (every 12h it checks; nginx reloads every 6h to pick up a
renewed cert). To eyeball it:

```bash
dcle exec certbot certbot certificates    # shows the cert and its expiry
```

---

## 7. Troubleshooting (quick pointers)

The full table is in **RUNBOOK.md → Troubleshooting**. The greatest hits:

| Symptom | First look |
|---|---|
| Site down after a redeploy | `dcle ps` — is `nginx` up? Did you use **`dcle`** (both `-f` files), not plain compose? |
| `502 Bad Gateway` | An upstream restarted; nginx re-resolves automatically, but `dcle restart nginx` forces it. Check `dcle ps` for `api`/`powersync` health. |
| `/health/ready` shows `powersync: missing` / `degraded` | Replication slot issue — RUNBOOK → "PowerSync — replication slot". Confirm `POWERSYNC_IMAGE_TAG=1.21.0`. |
| `/health/ready` returns 503 | Database unreachable — check `dcle logs postgres`. |
| Web app calls the wrong API host | `VITE_API_URL` is baked at build — `dcle build web-build && dcle up -d web-build` after any `DOMAIN` change. |
| Migration didn't apply | `dcle logs migrate` — it must exit `0`. |
| Login rejects a known-good password | You may have re-seeded (passwords come from `.env.prod`); or it's the platform vs bootstrap account mix-up (§0 table). |

---

## 8. Glossary (plain-English)

- **VPS** — a rented Linux server (yours is at Hetzner). You SSH into it.
- **Container** — an isolated, running instance of a packaged piece of software. **Image** — the
  packaged recipe a container runs from.
- **Docker Compose** — the tool that runs a whole set of containers together from `*.yaml` files.
- **Overlay** — an extra compose file layered on the base to change a few things (yours adds Let's
  Encrypt TLS). You apply it by passing both files with `-f` (the `dcle` alias).
- **Volume** — Docker-managed disk storage that **survives** container restarts/recreation (your DB,
  the compiled site, the TLS cert). `down -v` is the only normal way to delete one.
- **One-shot service** — a container that does a task and exits `0` (`migrate`, `web-build`). Seeing
  them "exited" in `dcle ps` is healthy.
- **Reverse proxy** — nginx: the single public entry point that terminates HTTPS and forwards requests
  to the right internal container by hostname.
- **envsubst** — nginx substitutes `${DOMAIN}`/`${SSL_CERT}` into its config template at container
  start. (Quirk: it only happens when the container's command is `nginx` — the overlay carefully
  preserves that.)
- **Migration** — a versioned database schema change. Applied by the `migrate` service before the API
  starts; safe to re-run (already-applied ones are skipped).
- **HTTP-01 challenge** — how Let's Encrypt verifies you control a hostname (serves a token file over
  port 80 before issuing the cert).
- **sslip.io** — free DNS where `<ip>.sslip.io` resolves to `<ip>`, giving you hostnames without owning
  a domain.

---

## 9. Where to go deeper

- **`RUNBOOK.md`** — the operational reference: first-time deploy (both Cloudflare and no-domain paths),
  redeploy, migrations, backups & restore, secret rotation, scaling Hangfire, observability (Sentry /
  Seq / Hangfire dashboard / uptime), and the full troubleshooting matrix.
- **`LAUNCH_CHECKLIST.md`** — the go-live sign-off checklist.
- **`.env.prod.example`** — every configurable key, documented inline.
- **`secrets/README.md`** — how to create the two secret files.
- **`vet-backend/CLAUDE.md`** — backend architecture + the alert thresholds reference.
- **`docker-compose.prod.yaml` / `docker-compose.letsencrypt.yaml` / `nginx/conf.d/vet.conf.template`** —
  the deployment itself; all three are heavily commented.
```
