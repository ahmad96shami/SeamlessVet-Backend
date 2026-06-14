#!/usr/bin/env bash
# Bootstrap Let's Encrypt certs for the direct-TLS (no-Cloudflare) deployment — e.g. a raw VPS IP via a
# free sslip.io hostname. Run ONCE on the VPS, from the vet-backend dir, with ports 80+443 reachable
# from the internet (open the firewall / Hetzner Cloud Firewall first).
#
#   DOMAIN=<your-ip>.sslip.io LETSENCRYPT_EMAIL=you@example.com ./scripts/init-letsencrypt.sh
#   # add --staging the FIRST time to avoid Let's Encrypt rate limits while you shake out DNS/ports:
#   DOMAIN=… LETSENCRYPT_EMAIL=… ./scripts/init-letsencrypt.sh --staging
#
# DOMAIN / LETSENCRYPT_EMAIL are read from .env.prod if not passed in the environment.
set -euo pipefail
cd "$(dirname "$0")/.."

COMPOSE="docker compose --env-file .env.prod -f docker-compose.prod.yaml -f docker-compose.letsencrypt.yaml"

# --- resolve config -----------------------------------------------------------
envget() { [ -f .env.prod ] && grep -E "^$1=" .env.prod | head -1 | cut -d= -f2- || true; }
DOMAIN="${DOMAIN:-$(envget DOMAIN)}"
LETSENCRYPT_EMAIL="${LETSENCRYPT_EMAIL:-$(envget LETSENCRYPT_EMAIL)}"
STAGING=""
[ "${1:-}" = "--staging" ] && STAGING="--staging"

if [ -z "$DOMAIN" ] || [ "$DOMAIN" = "example.com" ]; then
  echo "ERROR: set DOMAIN (e.g. DOMAIN=1.2.3.4.sslip.io) in the env or .env.prod." >&2; exit 1
fi
if [ -z "$LETSENCRYPT_EMAIL" ]; then
  echo "ERROR: set LETSENCRYPT_EMAIL (for expiry notices) in the env or .env.prod." >&2; exit 1
fi

PRIMARY="vet.$DOMAIN"
DOMAIN_ARGS="-d vet.$DOMAIN -d api.$DOMAIN -d sync.$DOMAIN -d $DOMAIN"
echo "Domain : $DOMAIN"
echo "SANs   : vet.$DOMAIN, api.$DOMAIN, sync.$DOMAIN, $DOMAIN"
echo "Email  : $LETSENCRYPT_EMAIL"
echo "Mode   : ${STAGING:-PRODUCTION (real cert)}"
echo

# --- 1. dummy cert so nginx can boot its :443 blocks before the real cert exists ---
echo "==> creating a temporary self-signed cert at live/$PRIMARY/ ..."
# --no-deps: don't start nginx (it depends on this cert existing first).
$COMPOSE run --rm --no-deps --entrypoint sh certbot -c "\
  mkdir -p /etc/letsencrypt/live/$PRIMARY && \
  openssl req -x509 -nodes -newkey rsa:2048 -days 1 \
    -keyout /etc/letsencrypt/live/$PRIMARY/privkey.pem \
    -out    /etc/letsencrypt/live/$PRIMARY/fullchain.pem \
    -subj '/CN=localhost'"

# --- 2. bring the stack up (nginx now serves :80 challenge + :443 with the dummy cert) ---
echo "==> starting the stack ..."
$COMPOSE up -d --build
$COMPOSE up -d --wait --wait-timeout 240 nginx || true

# --- 3. drop the dummy and request the real cert via the HTTP-01 webroot challenge ---
echo "==> removing the dummy cert ..."
$COMPOSE run --rm --no-deps --entrypoint sh certbot -c "\
  rm -Rf /etc/letsencrypt/live/$PRIMARY /etc/letsencrypt/archive/$PRIMARY /etc/letsencrypt/renewal/$PRIMARY.conf"

echo "==> requesting the Let's Encrypt cert ..."
$COMPOSE run --rm --no-deps --entrypoint certbot certbot certonly \
  --webroot -w /var/www/certbot \
  $STAGING --email "$LETSENCRYPT_EMAIL" --agree-tos --no-eff-email --force-renewal \
  $DOMAIN_ARGS

# --- 4. reload nginx with the real cert + start the renewal loop ---
echo "==> reloading nginx ..."
$COMPOSE exec nginx nginx -s reload
$COMPOSE up -d certbot

echo
echo "Done. Visit https://vet.$DOMAIN"
[ -n "$STAGING" ] && echo "NOTE: --staging cert is NOT browser-trusted. Re-run WITHOUT --staging for the real cert."
