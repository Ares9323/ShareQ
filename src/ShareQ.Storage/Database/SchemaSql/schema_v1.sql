-- ShareQ schema v1

PRAGMA journal_mode = WAL;
PRAGMA foreign_keys = ON;

CREATE TABLE schema_version (
    version INTEGER NOT NULL
);
INSERT INTO schema_version (version) VALUES (1);

CREATE TABLE items (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    kind            TEXT    NOT NULL,
    source          TEXT    NOT NULL,
    created_at      INTEGER NOT NULL,
    pinned          INTEGER NOT NULL DEFAULT 0,
    deleted_at      INTEGER,
    source_process  TEXT,
    source_window   TEXT,
    payload         BLOB    NOT NULL,
    payload_size    INTEGER NOT NULL,
    blob_ref        TEXT,
    uploaded_url    TEXT,
    uploader_id     TEXT,
    search_text     TEXT
);

CREATE INDEX idx_items_created
    ON items(created_at DESC)
    WHERE deleted_at IS NULL;

CREATE INDEX idx_items_kind
    ON items(kind, created_at DESC)
    WHERE deleted_at IS NULL;

CREATE INDEX idx_items_pinned
    ON items(pinned, created_at DESC)
    WHERE deleted_at IS NULL AND pinned = 1;

CREATE VIRTUAL TABLE items_fts USING fts5(
    search_text,
    content='items',
    content_rowid='id',
    tokenize='unicode61 remove_diacritics 2'
);

CREATE TRIGGER items_ai AFTER INSERT ON items BEGIN
    INSERT INTO items_fts(rowid, search_text) VALUES (new.id, new.search_text);
END;

CREATE TRIGGER items_ad AFTER DELETE ON items BEGIN
    INSERT INTO items_fts(items_fts, rowid, search_text) VALUES ('delete', old.id, old.search_text);
END;

CREATE TRIGGER items_au AFTER UPDATE ON items BEGIN
    INSERT INTO items_fts(items_fts, rowid, search_text) VALUES ('delete', old.id, old.search_text);
    INSERT INTO items_fts(rowid, search_text) VALUES (new.id, new.search_text);
END;

CREATE TABLE pipeline_profiles (
    id              TEXT PRIMARY KEY,
    display_name    TEXT NOT NULL,
    trigger         TEXT NOT NULL,
    tasks_json      TEXT NOT NULL
);

CREATE TABLE settings (
    key             TEXT PRIMARY KEY,
    value           TEXT NOT NULL,
    is_sensitive    INTEGER NOT NULL DEFAULT 0
);
