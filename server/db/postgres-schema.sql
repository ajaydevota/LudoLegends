-- Ludo Legends - PostgreSQL schema
--
-- Apply once to a fresh database:
--   psql "$LUDO_DATABASE_URL" -f server/db/postgres-schema.sql
--
-- The EF Core model in src/LudoLegends.Server/Data/GameDbContext.cs is authoritative;
-- this file mirrors it for production deployments that prefer explicit DDL (the server
-- runs with Ludo:Database:AutoCreate=false in production).
--
-- Safe to re-run: every statement is idempotent.

BEGIN;

CREATE TABLE IF NOT EXISTS accounts (
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

CREATE INDEX IF NOT EXISTS ix_accounts_trophies ON accounts (trophies DESC);

CREATE TABLE IF NOT EXISTS wallets (
  account_id varchar(40) PRIMARY KEY REFERENCES accounts(id) ON DELETE CASCADE,
  balance bigint NOT NULL DEFAULT 0 CHECK (balance >= 0),
  updated_utc timestamptz NOT NULL
);

-- Append-only coin ledger. The unique (account_id, idempotency_key) index is what makes
-- every reward and charge exactly-once, even under client retries and crashed requests.
CREATE TABLE IF NOT EXISTS coin_transactions (
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

CREATE INDEX IF NOT EXISTS ix_coin_account_time ON coin_transactions (account_id, created_utc DESC);

CREATE TABLE IF NOT EXISTS friendships (
  id varchar(40) PRIMARY KEY,
  requester_id varchar(40) NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  addressee_id varchar(40) NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  status int NOT NULL DEFAULT 0,
  created_utc timestamptz NOT NULL,
  responded_utc timestamptz,
  CONSTRAINT ux_friendship UNIQUE (requester_id, addressee_id)
);

CREATE INDEX IF NOT EXISTS ix_friendship_addressee ON friendships (addressee_id);

CREATE TABLE IF NOT EXISTS matches (
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

CREATE INDEX IF NOT EXISTS ix_matches_started ON matches (started_utc DESC);
CREATE INDEX IF NOT EXISTS ix_matches_winner ON matches (winner_account_id);

CREATE TABLE IF NOT EXISTS match_participants (
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

CREATE INDEX IF NOT EXISTS ix_participants_match ON match_participants (match_id);
CREATE INDEX IF NOT EXISTS ix_participants_account ON match_participants (account_id);

CREATE TABLE IF NOT EXISTS daily_claims (
  id varchar(40) PRIMARY KEY,
  account_id varchar(40) NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  claim_date varchar(10) NOT NULL,
  streak_day int NOT NULL,
  coins bigint NOT NULL,
  created_utc timestamptz NOT NULL,
  CONSTRAINT ux_daily_claim UNIQUE (account_id, claim_date)
);

CREATE TABLE IF NOT EXISTS mission_progress (
  id varchar(40) PRIMARY KEY,
  account_id varchar(40) NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  mission_key varchar(40) NOT NULL,
  period_key varchar(12) NOT NULL,
  progress int NOT NULL DEFAULT 0,
  claimed boolean NOT NULL DEFAULT false,
  updated_utc timestamptz NOT NULL,
  CONSTRAINT ux_mission UNIQUE (account_id, mission_key, period_key)
);

CREATE TABLE IF NOT EXISTS achievements (
  id varchar(40) PRIMARY KEY,
  account_id varchar(40) NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  achievement_key varchar(40) NOT NULL,
  unlocked_utc timestamptz NOT NULL,
  claimed boolean NOT NULL DEFAULT false,
  CONSTRAINT ux_achievement UNIQUE (account_id, achievement_key)
);

CREATE TABLE IF NOT EXISTS purchases (
  id varchar(40) PRIMARY KEY,
  account_id varchar(40) NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  item_id varchar(40) NOT NULL,
  coins_spent bigint NOT NULL,
  created_utc timestamptz NOT NULL,
  CONSTRAINT ux_purchase UNIQUE (account_id, item_id)
);

CREATE TABLE IF NOT EXISTS device_sessions (
  id varchar(40) PRIMARY KEY,
  account_id varchar(40) NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  token_hash varchar(64) NOT NULL UNIQUE,
  created_utc timestamptz NOT NULL,
  expires_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_sessions_account ON device_sessions (account_id);

COMMIT;
