# `secrets/` — production secrets (gitignored)

Nothing real in this directory is committed (see `.gitignore` here). On a fresh deploy you create
**two** things; everything else lives in `../.env.prod`.

## 1. PowerSync RSA signing keypair → `appsettings.Production.json`

The API mints PowerSync client tokens with an RSA key and publishes the matching public key at
`/.well-known/jwks.json`. **If this key is not set, the API generates an ephemeral one that changes on
every restart — every mobile client's sync token breaks after a redeploy.** So it must be stable.

```bash
# 2048-bit RSA private key, PKCS#8 (-----BEGIN PRIVATE KEY-----)
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out powersync-private.pem
# Matching public key (optional — the API derives the JWKS from the private key)
openssl rsa -in powersync-private.pem -pubout -out powersync-public.pem

# Build appsettings.Production.json with the PEMs JSON-escaped (\n for newlines):
cp appsettings.Production.example.json appsettings.Production.json
PRIV=$(jq -Rs . < powersync-private.pem)
PUB=$(jq -Rs . < powersync-public.pem)
jq --argjson priv "$PRIV" --argjson pub "$PUB" \
   '.PowerSync.SigningKey.PrivateKeyPem=$priv | .PowerSync.SigningKey.PublicKeyPem=$pub' \
   appsettings.Production.example.json > appsettings.Production.json

# Don't leave the raw PEMs lying around once they're in the JSON:
shred -u powersync-private.pem powersync-public.pem 2>/dev/null || rm -f powersync-private.pem powersync-public.pem
```

`Kid` is optional (the API derives a stable one from the key if you omit it), but setting an explicit
value documents key rotations.

## 2. Cloudflare Origin Certificate → `cloudflare-origin/`

nginx terminates TLS with a Cloudflare **Origin Certificate** (Cloudflare dashboard → SSL/TLS → Origin
Server → Create Certificate; cover `${DOMAIN}` **and** `*.${DOMAIN}` so both `api.` and `sync.` match).

```bash
mkdir -p cloudflare-origin
# Paste the certificate Cloudflare shows you:
$EDITOR cloudflare-origin/origin.pem   # the certificate (PEM)
$EDITOR cloudflare-origin/origin.key   # the private key (PEM) — shown only once
chmod 600 cloudflare-origin/origin.key
```

Set the Cloudflare SSL/TLS mode to **Full (Strict)** so the edge validates this origin cert.

## Result

```
secrets/
├── appsettings.Production.json     # PowerSync signing keypair (gitignored)
└── cloudflare-origin/
    ├── origin.pem                  # Cloudflare Origin Certificate (gitignored)
    └── origin.key                  # its private key (gitignored, chmod 600)
```
