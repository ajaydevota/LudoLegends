# Security model

## Trust boundaries

| Zone | Trust |
|---|---|
| Android app | **Untrusted.** May be modified, debugged, or driven by a script. |
| Network | **Untrusted.** Assume replay, tampering and dropped connections. |
| Server process | Trusted. Holds signing keys and the database credential. |
| PostgreSQL | Trusted; never reachable from the client. |

Every rule of the game and every unit of currency is decided behind the server boundary.

## Authentication

* **Guest**: device sends an installation id (random UUID, app-generated). The server keeps
  only its SHA-256, issues a normal account, and returns a session token. No personal data
  is collected.
* **Google**: the server verifies the ID token locally against Google's JWKS with RS256
  pinned, issuer checked, audience equal to the configured web client id, and expiry
  enforced. A guest account can be linked in place, preserving coins and stats.
* **Sessions** are self-issued HS256 JWTs (30 days): `sub`, `name`, `code`, `lvl`, `guest`,
  `jti`. The signing key is required from configuration; the server refuses to start in
  Production without one (development generates an ephemeral key).
* **No secret ships in the APK.** Only the server URL, the Google *web* client id and ad
  unit ids, all of which are public by design.

## Authorisation

* Every authorised endpoint derives the account from `sub`. There is no `?accountId=`
  parameter anywhere, so horizontal privilege escalation has no surface.
* Coin operations are only reachable through `CoinService`, which always binds the
  idempotency key to the caller's account id.
* Room membership gates game actions: `GameRoom.HandleCommand` rejects anyone who is not a
  member, and the engine rejects any action from a seat that is not active.

## Anti-cheat

| Attack | Defence |
|---|---|
| Client claims a die value | Protocol has no die field at all; the engine's roll is filled by a cryptographic RNG on the server. Covered by a test on `ClientEnvelope`. |
| Client moves a token illegally | The move is validated against the pending die, blocking, safe cells, exact-home and turn ownership before any state changes. |
| Client reports the result | Results come from the engine's finish order; settlement is server-side and idempotent per match id. |
| Replaying a command | `CommandId` dedupe inside the engine (last 64) plus per-command idempotency; a replayed roll cannot produce a second roll. |
| Double-spending coins | Set-based `UPDATE ... WHERE balance >= cost`, unique ledger key per operation. |
| Claiming rewards twice | Unique `(account, claim_date)` for daily, `(account, mission, period)` for missions, ledger keys for everything else. |
| Coin duplication via concurrency | Additive balance updates are atomic in SQL; ledger insert and balance change live in one transaction. |
| Bot/automation flooding | Token-bucket per socket (8 msg/s, burst 4×) + per-IP HTTP limit (120/min) + 4 KB message cap. |
| Faking ad views for coins | Rewarded ads do not grant currency in this build; all coins are server-granted by verified events. |
| Spoofing another player in a room | The socket connection is bound to the verified token; the account id is never read from message payloads. |

## Transport

* Terminate TLS at the reverse proxy (Caddy/nginx). The Android client targets
  `https://`/`wss://`; cleartext is not permitted for a release build.
* `ForwardedHeaders` is enabled so client IPs come from the proxy, not the socket.
* Session tokens travel in the `Authorization` header for REST and in the WebSocket
  handshake (query or header), never in URLs that get logged for REST.

## Data protection

* Passwords are never stored (there are none).
* Device ids: hashed, never raw.
* Emails: only for Google accounts, never exposed by any API response.
* Leaderboards expose display name, level, trophies and wins only.
* Logs: account ids and room codes, no tokens, no emails, no message bodies.
* Account deletion anonymises the account, releases `device_hash`/`google_subject`, deletes
  device sessions, and keeps financial rows without personal data.

## Rate limiting and input validation

* Display names: 3-16 chars, letters/digits/space/`_`/`-`, sanitised server-side.
* Room codes: 6 chars from an unambiguous alphabet, case-normalised.
* Avatars and emotes: whitelist-validated identifiers, length-capped.
* All JSON is size-capped before parsing; malformed payloads are rejected, never thrown.
* Unknown message types are rejected with a stable error code.

## Secrets checklist for deployment

- [ ] `LUDO__TOKENSIGNINGKEY` - 32+ random characters, unique per environment
- [ ] `LUDO__DATABASE__CONNECTIONSTRING` - SSL required; never committed
- [ ] env file owned by the service user, mode `600`
- [ ] Keystore outside the repo; passwords in CI secrets only
- [ ] Neon/Supabase/Google service accounts stored in the host's secret manager
- [ ] `LUDO__DATABASE__AUTOCREATE=false` in production
- [ ] Reverse proxy enforces HTTPS and forwards only to loopback

## Reporting

If you find a vulnerability, open a private report in the repository, or email the address
listed in the Play Store listing. Please do not publish exploit details before a fix ships.
