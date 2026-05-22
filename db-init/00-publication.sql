-- Runs once at container init (Postgres entrypoint reads /docker-entrypoint-initdb.d).
-- Publishes every table so PowerSync's replication slot can stream them; Sync Rules
-- decide what actually reaches each client. See docs/TECH_STACK.md "Enable logical replication".

CREATE PUBLICATION powersync FOR ALL TABLES;
