-- dotflow schema migration
-- Run via: dotnet dotflow migrate

CREATE SCHEMA IF NOT EXISTS dotflow;

CREATE TABLE IF NOT EXISTS dotflow.workflow_runs (
    id           TEXT        NOT NULL PRIMARY KEY,
    workflow_id  TEXT        NOT NULL,
    status       TEXT        NOT NULL,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    started_at   TIMESTAMPTZ,
    finished_at  TIMESTAMPTZ,
    input        JSONB       NOT NULL DEFAULT '{}'::jsonb,
    phases       JSONB       NOT NULL DEFAULT '[]'::jsonb
);

CREATE INDEX IF NOT EXISTS idx_workflow_runs_workflow_id ON dotflow.workflow_runs (workflow_id);
CREATE INDEX IF NOT EXISTS idx_workflow_runs_status      ON dotflow.workflow_runs (status);
CREATE INDEX IF NOT EXISTS idx_workflow_runs_created_at  ON dotflow.workflow_runs (created_at DESC);

CREATE TABLE IF NOT EXISTS dotflow.events (
    id          TEXT        NOT NULL PRIMARY KEY,
    run_id      TEXT        NOT NULL REFERENCES dotflow.workflow_runs(id) ON DELETE CASCADE,
    event_type  TEXT        NOT NULL,
    payload     JSONB       NOT NULL DEFAULT '{}'::jsonb,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_events_run_id      ON dotflow.events (run_id);
CREATE INDEX IF NOT EXISTS idx_events_occurred_at ON dotflow.events (occurred_at);
