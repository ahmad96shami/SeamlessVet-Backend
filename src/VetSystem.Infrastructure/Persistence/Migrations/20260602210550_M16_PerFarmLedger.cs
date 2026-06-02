using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M16_PerFarmLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_ledgers_customer",
                table: "ledgers");

            migrationBuilder.AddColumn<Guid>(
                name: "farm_id",
                table: "receipt_vouchers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "customer_id",
                table: "ledgers",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "farm_id",
                table: "ledgers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_receipt_vouchers_farm_id",
                table: "receipt_vouchers",
                column: "farm_id");

            migrationBuilder.CreateIndex(
                name: "ux_ledgers_customer",
                table: "ledgers",
                column: "customer_id",
                unique: true,
                filter: "customer_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_ledgers_farm",
                table: "ledgers",
                column: "farm_id",
                unique: true,
                filter: "farm_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledgers_owner",
                table: "ledgers",
                sql: "num_nonnulls(customer_id, farm_id) = 1");

            migrationBuilder.AddForeignKey(
                name: "fk_ledgers_farms_farm_id",
                table: "ledgers",
                column: "farm_id",
                principalTable: "farms",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_receipt_vouchers_farms_farm_id",
                table: "receipt_vouchers",
                column: "farm_id",
                principalTable: "farms",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // M16 — a farm ledger carries customer_id = NULL. Keep the M14 ledger_entries scope key
            // (the denormalized customer_id the `by_customer` sync bucket reads) correct by resolving a
            // farm ledger to its owning customer. Same trigger name; only the function body changes.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION vet_set_ledger_entry_scope() RETURNS trigger AS $$
                BEGIN
                    SELECT COALESCE(l.customer_id, f.customer_id)
                    INTO NEW.customer_id
                    FROM ledgers l
                    LEFT JOIN farms f ON f.id = l.farm_id
                    WHERE l.id = NEW.ledger_id;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            SplitPerFarmLedgers(migrationBuilder);
        }

        /// <summary>
        /// M16 part 2 (data) — split each customer ledger into per-farm ledgers without changing what
        /// anyone owes. Creates one ledger per existing farm, re-attributes each customer-ledger entry
        /// to its farm ledger (by the entry's source-invoice <c>farm_id</c>, falling back to the
        /// customer's sole farm — the universal post-M15 backfill shape — so payments/adjustments follow
        /// the farm their invoices moved to), recomputes each entry's <c>balance_after</c>, then sets a
        /// farm ledger's balance to Σ its entries and the customer's own ledger to its <b>pre-split
        /// balance − Σ its farm balances</b>. Anchoring the own ledger on the captured pre-split balance
        /// (not Σ entries) makes the split conserve the stored balance <i>exactly</i>, even for the rare
        /// ledger whose stored balance already diverged from its entries — a structural migration must
        /// not alter a customer's total. A hard gate then re-asserts conservation and that no entry
        /// crossed customers. Runs inside the migration transaction, so any failure rolls the milestone
        /// back. Entry re-attribution + balance_after recompute is the SCHEMA-sanctioned controlled
        /// exception to ledger_entries' append-only rule.
        /// </summary>
        private static void SplitPerFarmLedgers(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- Capture each customer ledger's pre-split balance — the amount the split must conserve.
CREATE TEMP TABLE _m16_old_balance ON COMMIT DROP AS
SELECT customer_id, balance AS old_balance
FROM ledgers
WHERE customer_id IS NOT NULL;

-- 1) One ledger per existing (active) farm. Balance/status are set in step 4.
INSERT INTO ledgers (id, farm_id, balance, status, environment_id, created_at, updated_at)
SELECT gen_random_uuid(), f.id, 0, 'open', f.environment_id, now(), now()
FROM farms f
WHERE f.deleted_at IS NULL
  AND NOT EXISTS (SELECT 1 FROM ledgers l WHERE l.farm_id = f.id);

-- 2) Re-attribute each customer-ledger entry to its farm ledger. Target farm =
--    the entry's source-invoice farm_id, else (when the owning customer has exactly one farm —
--    the universal post-M15 backfill case) that sole farm, so payments/adjustments follow the
--    farm their invoices moved to. Multi-farm customers (none at migration time) keep unattributed
--    entries on the customer ledger. Entries only ever move between a customer and that same
--    customer's farms (asserted in step 5).
WITH single_farm AS (
    -- exactly one farm (HAVING count = 1), so array_agg[1] is that sole farm (no min(uuid) in PG17).
    SELECT customer_id, (array_agg(id))[1] AS farm_id
    FROM farms
    WHERE deleted_at IS NULL
    GROUP BY customer_id
    HAVING count(*) = 1
),
target AS (
    SELECT le.id AS entry_id,
           coalesce(inv.farm_id, sf.farm_id) AS target_farm_id
    FROM ledger_entries le
    JOIN ledgers l ON l.id = le.ledger_id AND l.customer_id IS NOT NULL
    LEFT JOIN invoices inv ON inv.id = le.invoice_id
    LEFT JOIN single_farm sf ON sf.customer_id = l.customer_id
)
UPDATE ledger_entries le
SET ledger_id = fl.id
FROM target t
JOIN ledgers fl ON fl.farm_id = t.target_farm_id
WHERE le.id = t.entry_id
  AND t.target_farm_id IS NOT NULL;

-- 3) Recompute balance_after per (new) ledger, in chronological order.
WITH ordered AS (
    SELECT id,
           sum(amount) OVER (PARTITION BY ledger_id ORDER BY created_at, id
                             ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS running
    FROM ledger_entries
)
UPDATE ledger_entries le
SET balance_after = o.running
FROM ordered o
WHERE le.id = o.id;

-- 4a) Farm ledger balance = Σ its re-attributed entries.
UPDATE ledgers l
SET balance = coalesce(s.bal, 0)
FROM (SELECT ledger_id, sum(amount) AS bal FROM ledger_entries GROUP BY ledger_id) s
WHERE l.id = s.ledger_id AND l.farm_id IS NOT NULL;

-- 4b) Customer own ledger balance = pre-split balance − Σ its farm-ledger balances. Anchoring on the
--     captured balance (not Σ remaining entries) conserves the stored total exactly.
UPDATE ledgers l
SET balance = ob.old_balance - coalesce(ff.farm_sum, 0)
FROM _m16_old_balance ob
LEFT JOIN (
    SELECT f.customer_id, sum(fl.balance) AS farm_sum
    FROM ledgers fl JOIN farms f ON f.id = fl.farm_id
    GROUP BY f.customer_id
) ff ON ff.customer_id = ob.customer_id
WHERE l.customer_id = ob.customer_id;

-- 4c) Derive status from the new balance (a closed ledger stays closed).
UPDATE ledgers l
SET status = CASE WHEN l.status = 'closed' THEN 'closed'
                  WHEN l.balance > 0 THEN 'has_debt'
                  ELSE 'open' END;

-- 5) Hard gates (roll the milestone back on any breach):
--    (a) no entry crossed customers — every farm-ledger entry's owning-customer scope key
--        (the M14 denormalized ledger_entries.customer_id) equals the farm's customer;
--    (b) money is conserved — own + Σ farms == pre-split balance for every customer.
DO $$
DECLARE leaked int; mismatched int;
BEGIN
    SELECT count(*) INTO leaked
    FROM ledger_entries le
    JOIN ledgers fl ON fl.id = le.ledger_id AND fl.farm_id IS NOT NULL
    JOIN farms f ON f.id = fl.farm_id
    WHERE le.customer_id IS DISTINCT FROM f.customer_id;
    IF leaked > 0 THEN
        RAISE EXCEPTION 'M16 split moved % ledger entr(ies) across customers', leaked;
    END IF;

    SELECT count(*) INTO mismatched
    FROM _m16_old_balance ob
    WHERE ob.old_balance <> (
        SELECT coalesce(sum(l.balance), 0)
        FROM ledgers l
        LEFT JOIN farms f ON f.id = l.farm_id
        WHERE l.customer_id = ob.customer_id
           OR f.customer_id = ob.customer_id
    );
    IF mismatched > 0 THEN
        RAISE EXCEPTION 'M16 per-farm ledger split failed balance conservation for % customer(s)', mismatched;
    END IF;
END $$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-merge farm ledgers back into the customer ledger before the DDL (which drops farm_id
            // and re-asserts customer_id NOT NULL — both require no farm-owned ledgers to remain).
            // Restore each customer's balance to own + Σ its farm balances (the inverse of the split's
            // anchoring), so a ledger whose stored balance pre-existingly diverged from its entries is
            // preserved rather than silently recomputed.
            migrationBuilder.Sql(@"
UPDATE ledgers cl
SET balance = cl.balance + ff.farm_sum,
    status = CASE WHEN cl.status = 'closed' THEN 'closed'
                  WHEN cl.balance + ff.farm_sum > 0 THEN 'has_debt' ELSE 'open' END
FROM (SELECT f.customer_id, sum(fl.balance) AS farm_sum
      FROM ledgers fl JOIN farms f ON f.id = fl.farm_id
      GROUP BY f.customer_id) ff
WHERE cl.customer_id = ff.customer_id;

UPDATE ledger_entries le
SET ledger_id = cl.id
FROM ledgers fl
JOIN farms f ON f.id = fl.farm_id
JOIN ledgers cl ON cl.customer_id = f.customer_id
WHERE le.ledger_id = fl.id;

DELETE FROM ledgers WHERE farm_id IS NOT NULL;

WITH ordered AS (
    SELECT id,
           sum(amount) OVER (PARTITION BY ledger_id ORDER BY created_at, id
                             ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS running
    FROM ledger_entries
)
UPDATE ledger_entries le SET balance_after = o.running FROM ordered o WHERE le.id = o.id;");

            // Restore the M14 trigger body (customer-ledger lookup only).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION vet_set_ledger_entry_scope() RETURNS trigger AS $$
                BEGIN
                    SELECT l.customer_id INTO NEW.customer_id FROM ledgers l WHERE l.id = NEW.ledger_id;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_ledgers_farms_farm_id",
                table: "ledgers");

            migrationBuilder.DropForeignKey(
                name: "fk_receipt_vouchers_farms_farm_id",
                table: "receipt_vouchers");

            migrationBuilder.DropIndex(
                name: "ix_receipt_vouchers_farm_id",
                table: "receipt_vouchers");

            migrationBuilder.DropIndex(
                name: "ux_ledgers_customer",
                table: "ledgers");

            migrationBuilder.DropIndex(
                name: "ux_ledgers_farm",
                table: "ledgers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledgers_owner",
                table: "ledgers");

            migrationBuilder.DropColumn(
                name: "farm_id",
                table: "receipt_vouchers");

            migrationBuilder.DropColumn(
                name: "farm_id",
                table: "ledgers");

            migrationBuilder.AlterColumn<Guid>(
                name: "customer_id",
                table: "ledgers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_ledgers_customer",
                table: "ledgers",
                column: "customer_id",
                unique: true);
        }
    }
}
