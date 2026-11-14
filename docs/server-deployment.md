# Server deployment and hosting costs

## What the server needs

| Requirement | Why |
|---|---|
| A long-lived process with WebSockets | matches live in memory; serverless functions cannot hold a socket |
| ~256 MB RAM, 0.5 vCPU | thousands of rooms fit comfortably; Ludo state is tiny |
| PostgreSQL (1 GB is plenty to start) | accounts, ledger, matches, social |
| HTTPS/WSS with a real certificate | the Android client refuses cleartext by default |
| Outbound internet | Google JWKS (sign-in), optional FCM/Photon |

## Honest free-tier analysis (checked November 2026)

**No managed platform gives you an always-on WebSocket process for $0 with no caveats.**
Here is the real picture:

| Option | Free? | Catch |
|---|---|---|
| **Oracle Cloud Always Free** (Ampere A1) | Yes, indefinitely | 2 OCPU / 12 GB ARM always-free allowance; needs a card for identity, capacity for Ampere is region-dependent, you operate the VM |
| **Neon** (PostgreSQL) | Yes | 1 GB storage, compute scales to zero after 5 min idle (first query wakes it), 2 CU max |
| **Supabase** (PostgreSQL) | Yes | 500 MB database, project pauses after ~1 week of inactivity |
| **Render / Railway / Fly.io** | Trial-ish | free/starter instances sleep or are time-limited; not suitable for live sockets |
| **Photon Fusion** | Free **100 CCU** plan | *Games only, one app per customer, hard-capped at 100 CCU* (≈40k MAU). Also a 20 CCU development tier. Photon is excellent for peer/hosted relay but our anti-cheat requires an authoritative server we control - Fusion's server plugins are Enterprise-tier |
| **Firebase** | Yes | Realtime Database/Firestore free tier is fine for coin/profile data, but it does **not** run an authoritative game loop; you would need Cloud Functions (cold starts, invocation quotas) |
| **Cloudflare Workers** | Yes | No long-lived WebSockets for game loops on the free plan (Durable Objects need a paid plan) |

### Recommended $0 starting stack

1. **Oracle Cloud Always Free** VM (ARM, 2 OCPU / 12 GB) running the .NET server under
   systemd, Caddy in front for automatic TLS.
2. **Neon free** PostgreSQL for data (1 GB, scale-to-zero is fine between matches).
3. Optional later: Photon Voice free tier for voice, Photon 100 CCU if you ever move
   transport to Photon.

This is genuinely free at small scale. The trade-off is that you administer a VM.

### When it stops being free

Rough thresholds where you must pay:

* Oracle: exceeding the always-free compute allowance (more/bigger instances), or block
  storage beyond 200 GB.
* Neon: >1 GB storage or sustained compute beyond the free allowance.
* Your own domain + email + Play Console ($25 one-off) + Play signing.

Put monitoring on disk usage and DB size first; those are what grow.

## Deploy step by step (Oracle / any Ubuntu 24.04 VM)

```bash
# 1. prerequisites
sudo apt update && sudo apt install -y dotnet-sdk-8.0 caddy

# 2. publish
git clone <your repo> /opt/ludo && cd /opt/ludo
dotnet publish server/src/LudoLegends.Server -c Release -o /opt/ludo/publish

# 3. configuration (never commit this file)
sudo mkdir -p /etc/ludo
sudo tee /etc/ludo/ludo.env >/dev/null <<'EOF'
LUDO__TOKENSIGNINGKEY=<openssl rand -base64 48>
LUDO__DATABASE__PROVIDER=postgres
LUDO__DATABASE__CONNECTIONSTRING=Host=...;Database=ludo;Username=...;Password=...;SSL Mode=Require
LUDO__DATABASE__AUTOCREATE=false
LUDO__PUBLICBASEURL=https://ludo.example.com
ASPNETCORE_URLS=http://127.0.0.1:5080
ASPNETCORE_ENVIRONMENT=Production
EOF
sudo chmod 600 /etc/ludo/ludo.env

# 4. schema (Neon dashboard SQL editor, or psql)
psql "$LUDO_DATABASE_URL" -f server/db/postgres-schema.sql

# 5. service
sudo tee /etc/systemd/system/ludo.service >/dev/null <<'EOF'
[Unit]
Description=Ludo Legends server
After=network.target

[Service]
WorkingDirectory=/opt/ludo/publish
EnvironmentFile=/etc/ludo/ludo.env
ExecStart=/usr/bin/dotnet /opt/ludo/publish/LudoLegends.Server.dll
Restart=always
RestartSec=3
User=www-data
# hardening
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ReadWritePaths=/opt/ludo/publish

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload && sudo systemctl enable --now ludo
sudo journalctl -u ludo -f          # logs

# 6. TLS + reverse proxy (Caddy handles certificates automatically)
sudo tee /etc/caddy/Caddyfile >/dev/null <<'EOF'
ludo.example.com {
    encode gzip
    reverse_proxy 127.0.0.1:5080
}
EOF
sudo systemctl reload caddy
```

### Verify a deployment

```bash
curl -s https://ludo.example.com/health                  # {"status":"ok",...}
curl -s -X POST https://ludo.example.com/api/auth/guest \
  -H 'Content-Type: application/json' \
  -d '{"deviceId":"deploy-check-0001","displayName":"Deploy"}'
# then: websocat "wss://ludo.example.com/ws?token=<token>"   (should print welcome)
```

## Operational notes

* **Restarts are safe.** Running matches are reconciled on boot
  (`RecoverStaleAsync` refunds fees for matches older than two hours and never double-pays).
* **Backups**: `pg_dump` daily is enough. The ledger is append-only, so point-in-time restore
  keeps coins consistent.
* **Scaling out** requires sticky sessions or a room-ownership map plus a shared presence
  store; see `docs/architecture.md`. Do not run two instances behind a plain round-robin
  balancer without that work.
* **Health checks**: `GET /health` (no auth) is safe for an uptime monitor.
* **Rate limiting** is per-IP at the HTTP layer and per-socket for game messages; tune with
  `LUDO__GAME__MESSAGESPERSECOND`.
