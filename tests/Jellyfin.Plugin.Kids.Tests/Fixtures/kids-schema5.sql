-- A real kids.db at schema 5, written by the Kids plugin 0.11 (main, d5e3017) on a dev server: one kid with a
-- 60-minute allowance, 30 s played and a 15-minute bonus. Dumped with sqlite3 .dump; user_version added at the end.
PRAGMA foreign_keys=OFF;
BEGIN TRANSACTION;
CREATE TABLE kid_profile (
  user_id    TEXT    PRIMARY KEY,
  revision   INTEGER NOT NULL CHECK (revision >= 1),
  settings   TEXT    NOT NULL,
  created_at TEXT    NOT NULL,
  updated_at TEXT    NOT NULL
);
INSERT INTO kid_profile VALUES('bb06175385d34dee9f4fd064a13dd904',1,'{"SearchAllowed":false,"AutoplayEnabled":false,"AutoplayCap":3,"Sections":[{"Section":"KeepWatching","Visible":true},{"Section":"Picked","Visible":true},{"Section":"Libraries","Visible":true},{"Section":"New","Visible":true},{"Section":"Favourites","Visible":true}],"PickedItemIds":[],"DailyAllowanceMinutes":60,"SessionLimitMinutes":null,"LiveTvChannelIds":[],"Avatar":null}','2026-11-25T00:26:21.3084564Z','2026-11-25T00:26:21.3084564Z');
CREATE TABLE parent_lock (
  id         INTEGER PRIMARY KEY CHECK (id = 1),
  lock_type  TEXT    NOT NULL CHECK (lock_type IN ('NUMERIC','REMOTE')),
  verifier   TEXT    NOT NULL,
  changed_at TEXT    NOT NULL
);
CREATE TABLE usage_day (
  user_id TEXT NOT NULL, day TEXT NOT NULL, used_ms INTEGER NOT NULL CHECK (used_ms >= 0),
  PRIMARY KEY (user_id, day));
INSERT INTO usage_day VALUES('bb06175385d34dee9f4fd064a13dd904','2026-11-25',30638);
CREATE TABLE reported_segment (
  user_id TEXT NOT NULL, segment_id TEXT NOT NULL, day TEXT NOT NULL, started_at TEXT NOT NULL,
  seconds INTEGER NOT NULL, received_at TEXT NOT NULL, PRIMARY KEY (user_id, segment_id));
CREATE TABLE IF NOT EXISTS "bonus" (
  id INTEGER PRIMARY KEY AUTOINCREMENT, user_id TEXT NOT NULL, day TEXT NOT NULL,
  minutes INTEGER NOT NULL CHECK (minutes BETWEEN 1 AND 240), granted_by TEXT NOT NULL, created_at TEXT NOT NULL);
INSERT INTO bonus VALUES(1,'bb06175385d34dee9f4fd064a13dd904','2026-11-25',15,'admin','2026-11-25T00:26:53.3088259Z');
PRAGMA writable_schema=ON;
CREATE TABLE IF NOT EXISTS sqlite_sequence(name,seq);
DELETE FROM sqlite_sequence;
INSERT INTO sqlite_sequence VALUES('bonus',1);
CREATE INDEX bonus_user_day ON bonus (user_id, day);
PRAGMA writable_schema=OFF;
COMMIT;
PRAGMA user_version = 5;
