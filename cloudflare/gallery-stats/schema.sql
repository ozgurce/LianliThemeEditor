CREATE TABLE IF NOT EXISTS theme_stats (
  theme_id TEXT PRIMARY KEY,
  downloads INTEGER NOT NULL DEFAULT 0,
  vote_count INTEGER NOT NULL DEFAULT 0,
  vote_total INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS theme_votes (
  theme_id TEXT NOT NULL,
  voter_key TEXT NOT NULL,
  rating INTEGER NOT NULL CHECK (rating BETWEEN 1 AND 5),
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (theme_id, voter_key)
);

CREATE INDEX IF NOT EXISTS idx_theme_votes_theme_id ON theme_votes(theme_id);

CREATE TABLE IF NOT EXISTS theme_submissions (
  id TEXT PRIMARY KEY,
  theme_name TEXT NOT NULL,
  author TEXT NOT NULL,
  contact TEXT NOT NULL DEFAULT '',
  device_model TEXT NOT NULL,
  description TEXT NOT NULL DEFAULT '',
  package_key TEXT NOT NULL,
  package_file_name TEXT NOT NULL,
  package_size INTEGER NOT NULL DEFAULT 0,
  preview_key TEXT NOT NULL DEFAULT '',
  preview_file_name TEXT NOT NULL DEFAULT '',
  status TEXT NOT NULL DEFAULT 'pending',
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  reviewed_at TEXT NOT NULL DEFAULT '',
  review_note TEXT NOT NULL DEFAULT ''
);

CREATE INDEX IF NOT EXISTS idx_theme_submissions_status ON theme_submissions(status, created_at);
