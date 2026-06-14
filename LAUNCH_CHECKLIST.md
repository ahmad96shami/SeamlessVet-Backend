# VetSystem — Production Launch Checklist (M14)

Run top-to-bottom against the target environment. **Dry-run against staging first** (M14 task 11), then
production. Deploy/operate steps live in `RUNBOOK.md`. Tick each box; record evidence inline.

Environment: ☐ staging ☐ production   Date: __________   Operator: __________

---

## 0. Pre-flight

- [ ] `dc ps` shows `postgres` (healthy), `api` (healthy), `powersync` (up), `nginx` (up); `migrate`
      and `web-build` both exited 0.
- [ ] `.env.prod` filled; `secrets/appsettings.Production.json` + `secrets/cloudflare-origin/*` present.
- [ ] `../vet-frontend` checked out alongside this repo (the `web-build` build context).
- [ ] Cloudflare SSL/TLS mode = **Full (Strict)**; `vet.<domain>`, `api.<domain>`, and `sync.<domain>`
      resolve (proxied).
- [ ] Bootstrap admin password changed from the seed value.

## 1. Smoke tests (through Cloudflare, `https://api.<domain>`)

- [ ] **Web app:** `https://vet.<domain>` serves the SPA (RTL Arabic login); login as admin reaches the
      shell; the browser Network tab shows API calls going to `https://api.<domain>` (CORS clean); the
      PWA install prompt / offline shell works.
- [ ] **Health:** `/health/live` → `{"status":"ok"}`; `/health/ready` → `status:"ok"`
      (all of database/hangfire/powersync `ok`).
- [ ] **Auth:** log in as admin → access + refresh tokens issued; `/auth/refresh` rotates; bad password
      rejected with the canonical `{code,message}`.
- [ ] **Admin approval:** register a test user → `inactive`; approve → `number_prefix` set, login works.
- [ ] **Visit:** create an in-clinic visit and a field visit; both persist with non-colliding
      `visit_number`s.
- [ ] **POS:** issue an invoice with mixed payments → totals reconcile; a ledger entry is posted; an
      idempotency-key replay returns the same invoice (no duplicate).
- [ ] **Sync write path:** `POST /sync/{table}` with a client GUID v7 + `Idempotency-Key` succeeds;
      replay returns the cached result (no double-apply); an absolute `stock_items` write is rejected.
- [ ] **Attachments (R2):** request a presigned upload URL, PUT a file, PATCH the key → a 5-min signed
      GET returns the object. DB stores only the key.

## 2. Background jobs

- [ ] Hangfire dashboard reachable at `https://api.<domain>/hangfire` **and gated** (non-admin → 403).
- [ ] Recurring jobs registered: `vaccination-reminders`, `low-stock-alerts`, `expiration-warnings`,
      `scheduled-report-delivery`.
- [ ] No jobs in the **Failed** state.

## 3. PowerSync (mobile read path)

> Sync rules are JOIN-free (parameter buckets + denormalized scope keys, M14) and validated to load.
> Confirm the live behavior here before shipping the mobile client.

- [ ] Replication slot present + active: `pg_replication_slots` shows one `pgoutput` slot, `active=t`.
- [ ] PowerSync logs show `Service started` with no `Fatal error` / `Must SELECT from a single table`.
- [ ] A device for doctor *D* syncs only *D*'s assigned customers/visits/inventory (per-doctor scoping)
      and **no** other doctor's rows — including the deep tables (ledger_entries, invoice_items,
      payments, vaccinations) whose scope keys are trigger-derived.

## 4. Logs & monitoring

- [ ] Serilog clean: no `@Level = 'Error'` noise in `dc logs api` / the `api-logs` volume on a normal
      session.
- [ ] **Sentry** enabled (`Sentry__Dsn` set in `.env.prod`) and quiet: clean boot, no unexpected issues
      in the project; `environment` + `release` tags correct. Alert rules per `CLAUDE.md` →
      "Dashboards & alert thresholds".
- [ ] Uptime monitor pings `https://api.<domain>/health/ready` (`503` ⇒ DB down; `degraded` ⇒ Hangfire
      or PowerSync slot down).

## 5. Backups & restore

- [ ] `./scripts/pg_dump_to_r2.sh` run once by hand → object appears in the R2 backups bucket.
- [ ] Cron installed (`/etc/cron.d/vet-backup`); R2 lifecycle rule = delete > 30 days.
- [ ] **Restore drill executed** (`scripts/restore_from_r2.sh --latest --target-db vet_restore_test
      --yes`): table/row counts match source. Time recorded in `RUNBOOK.md` restore-drill table.
- [ ] Weekly VPS snapshot configured (or manual procedure documented in `RUNBOOK.md`).
- [ ] **Clean-VPS-to-working target:** a fresh VPS reaches a working stack from the runbook in **< 1
      hour** (time it during the staging dry-run): __________.

## 6. Security

- [ ] Postgres **not** published to the host (`dc ps` shows no `0.0.0.0:5432`).
- [ ] No secrets committed: `git status` clean of `.env.prod` / `secrets/*` real files.
- [ ] `dotnet list package --vulnerable` clean (no vulnerable transitives).

---

## Sign-off (M14 task 12)

Launch is approved only when §1–§2, §4–§6 pass and §3 either passes or is explicitly deferred with the
mobile client held back.

| Role | Name | ✅ / notes | Date |
|---|---|---|---|
| Developer | | | |
| Client | | | |
