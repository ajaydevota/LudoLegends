# Database schema

The **EF Core model in `server/src/LudoLegends.Server/Data/` is authoritative**. SQLite is
used for development and tests (`EnsureCreated`), PostgreSQL for production. The DDL below
matches the model and is what `server/db/postgres-schema.sql` contains.

## Tables

### accounts
One row per player. Guests and Google accounts share the table; a guest can be upgraded in
place so coins and stats survive.

| column | type | notes |
|---|---|---|
| id | varchar(40) PK | GUID ("N") |
| player_code | varchar(12) UNIQUE | public friend code, 6 chars |
| display_name | varchar(24) | validated 3-16 chars |
| avatar_id | varchar(32) | preset, resolved client-side |
| is_guest | boolean | |
| device_hash | varchar(64) UNIQUE NULL | SHA-256 of installation id, never the raw id |
| google_subject | varchar(64) UNIQUE NULL | Google `sub` |
| email | varchar(160) NULL | Google accounts only |
| created_utc / last_seen_utc | timestamptz | |
| deleted_utc | timestamptz NULL | soft delete; identifiers are released |
| level / xp | int | level = 1 + xp/200, capped at 100 |
| games_played / wins / losses / abandoned | int | |
| trophies | int | leaderboard metric |
| unlocked_items | varchar(512) | comma separated item ids |
| ads_removed | boolean | |

### wallets
`account_id PK`, `balance bigint`, `updated_utc`.

### coin_transactions  (*the economy's integrity anchor*)
`id PK`, `account_id`, `delta`, `balance_after`, `reason`, `idempotency_key`,
`match_id NULL`, `created_utc`.

* **UNIQUE (account_id, idempotency_key)** - a reward or charge can never be applied twice.
* INDEX (account_id, created_utc) for statements.

### friendships
`id PK`, `requester_id`, `addressee_id`, `status` (0 pending, 1 accepted, 2 declined),
`created_utc`, `responded_utc`.
UNIQUE (requester_id, addressee_id).

### matches
`id PK`, `room_code`, `seat_count`, `entry_fee`, `pot`, `winner_account_id`, `status`
(0 running, 1 finished, 2 aborted), `started_utc`, `ended_utc`.

### match_participants
`id PK`, `match_id`, `account_id NULL`, `display_name`, `seat`, `color`, `is_bot`, `rank`,
`tokens_home`, `sixes_rolled`, `coins_delta`, `disconnected`.

### daily_claims
`id PK`, `account_id`, `claim_date` (yyyy-MM-dd), `streak_day`, `coins`, `created_utc`.
**UNIQUE (account_id, claim_date)** - the database, not the client, enforces one per day.

### mission_progress
`id PK`, `account_id`, `mission_key`, `period_key` (yyyy-MM-dd or yyyy-Www), `progress`,
`claimed`, `updated_utc`. UNIQUE (account_id, mission_key, period_key).

### achievements
`id PK`, `account_id`, `achievement_key`, `unlocked_utc`, `claimed`.
UNIQUE (account_id, achievement_key).

### purchases
`id PK`, `account_id`, `item_id`, `coins_spent`, `created_utc`.
UNIQUE (account_id, item_id) - one-time cosmetics.

### device_sessions
`id PK`, `account_id`, `token_hash` UNIQUE, `created_utc`, `expires_utc`. Reserved for
server-side session revocation; the current build uses stateless HS256 tokens.

## PostgreSQL DDL

```sql
CREATE TABLE accounts (
  id varchar(40) PRIMARY KEY,
  player_code varchar(12) NOT NULL UNIQUE,
  display_name varchar(24) NOT NULL,
  avatar_id varchar(32),
  is_guest boolean NOT NULL DEFAULT false,
  device_hash varchar(64) UNIQUE,
  google_subject varchar(64) UNIQUE,
  email varchar(160),
  created_utc timestamptz NOT NULL,
  last_seen_utc timestamptz NOT NULL,
  deleted_utc timestamptz,
  level int NOT NULL DEFAULT 1,
  xp int NOT NULL DEFAULT 0,
  games_played int NOT NULL DEFAULT 0,
  wins int NOT NULL DEFAULT 0,
  losses int NOT NULL DEFAULT 0,
  abandoned int NOT NULL DEFAULT 0,
  trophies int NOT NULL DEFAULT 0,
  unlocked_items varchar(512) NOT NULL DEFAULT '',
  ads_removed boolean NOT NULL DEFAULT false
);
CREATE INDEX ix_accounts_trophies ON accounts (trophies DESC);

CREATE TABLE wallets (
  account_id varchar(40) PRIMARY KEY REFERENCES accounts(id) ON DELETE CASCADE,
  balance bigint NOT NULL DEFAULT 0 CHECK (balance >= 0),
  updated_utc timestamptz NOT NULL
);

CREATE TABLE coin_transactions (
  id varchar(40) PRIMARY KEY,
  account_id varchar(40) NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  delta bigint NOT NULL,
  balance_after bigint NOT NULL,
  reason varchar(64) NOT NULL,
  idempotency_key varchar(120) NOT NULL,
  match_id varchar(40),
  created_utc timestamptz NOT NULL,
  CONSTRAINT ux_coin_idempotency UNIQUE (account_id, idempotency_key)
);
CREATE INDEX ix_coin_account_time ON coin_transactions (account_id, created_utc DESC);

CREATE TABLE friendships (
  id varchar(40) PRIMARY KEY,
  requester_id varchar(40) NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  addressee_id varchar(40) NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  status int NOT NULL DEFAULT 0,
  created_utc timestamptz NOT NULL,
  responded_utc timestamptz,
  CONSTRAINT ux_friendship UNIQUE (requester_id, addressee_id)
);
CREATE INDEX ix_friendship_addressee ON friendships (addressee_id);

CREATE TABLE matches (
  id varchar(40) PRIMARY KEY,
  room_code varchar(12),
  seat_count int NOT NULL,
  entry_fee bigint NOT NULL DEFAULT 0,
  pot bigint NOT NULL DEFAULT 0,
  winner_account_id varchar(40),
  status int NOT NULL DEFAULT 0,
  started_utc timestamptz NOT NULL,
  ended_utc timestamptz
);
CREATE INDEX ix_matches_started ON matches (started_utc DESC);

CREATE TABLE match_participants (
  id varchar(40) PRIMARY KEY,
  match_id varchar(40) NOT NULL REFERENCES matches(id) ON DELETE CASCADE,
  account_id varchar(40),
  display_name varchar(24),
  seat int NOT NULL,
  color int NOT NULL,
  is_bot boolean NOT NULL DEFAULT false,
  rank int NOT NULL DEFAULT 0,
  tokens_home int NOT NULL DEFAULT 0,
  sixes_rolled int NOT NULL DEFAULT 0,
  coins_delta bigint NOT NULL DEFAULT 0,
  disconnected boolean NOT NULL DEFAULT false
);
CREATE INDEX ix_participants_match ON match_participants (match_id);
CREATE INDEX ix_participants_account ON match_participants (account_id);

CREATE TABLE daily_claims (
  id varchar(40) PRIMARY KEY,
  account_id varchar(40) NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  claim_date varchar(10) NOT NULL,
  streak_day int NOT NULL,
  coins bigint NOT NULL,
  created_utc timestamptz NOT NULL,
  CONSTRAINT ux_daily_claim UNIQUE (account_id, claim_date)
);

CREATE TABLE mission_progress (
  id varchar(40) PRIMARY KEY,
  account_id varchar(40) NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  mission_key varchar(40) NOT NULL,
  period_key varchar(12) NOT NULL,
  progress int NOT NULL DEFAULT 0,
  claimed boolean NOT NULL DEFAULT false,
  updated_utc timestamptz NOT NULL,
  CONSTRAINT ux_mission UNIQUE (account_id, mission_key, period_key)
);

CREATE TABLE achievements (
  id varchar(40) PRIMARY KEY,
  account_id varchar(40) NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  achievement_key varchar(40) NOT NULL,
  unlocked_utc timestamptz NOT NULL,
  claimed boolean NOT NULL DEFAULT false,
  CONSTRAINT ux_achievement UNIQUE (account_id, achievement_key)
);

CREATE TABLE purchases (
  id varchar(40) PRIMARY KEY,
  account_id varchar(40) NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  item_id varchar(40) NOT NULL,
  coins_spent bigint NOT NULL,
  created_utc timestamptz NOT NULL,
  CONSTRAINT ux_purchase UNIQUE (account_id, item_id)
);

CREATE TABLE device_sessions (
  id varchar(40) PRIMARY KEY,
  account_id varchar(40) NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  token_hash varchar(64) NOT NULL UNIQUE,
  created_utc timestamptz NOT NULL,
  expires_utc timestamptz NOT NULL
);
CREATE INDEX ix_sessions_account ON device_sessions (account_id);
```

## Access rules

The database is **never** reachable from the Android client. There is no direct DB
credential in the app, no Firebase-style rules file to get wrong, and no public endpoint
that accepts an account id it did not derive from a verified token. Application-level rules:

1. Every `/api/me`, `/api/wallet`, `/api/shop`, `/api/friends`, `/api/history` call derives
   the account id from the JWT `sub` claim only.
2. Coin mutations happen exclusively inside `CoinService`, always with an idempotency key.
3. Match settlement happens exclusively inside `MatchService`, keyed by match id.
4. Read-only leaderboard queries expose display name, level and trophies - never email,
   device hash or Google subject.
5. Account deletion anonymises the row and releases `device_hash` / `google_subject`, so the
   same identity can start over; financial rows remain for audit without personal data.
