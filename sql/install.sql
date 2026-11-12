\set ON_ERROR_STOP on
-- Run in a NEW, dedicated database as its administrative owner.
BEGIN;
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'store') THEN
    RAISE EXCEPTION 'Schema store already exists. Installation stopped without changes.';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'store_owner') THEN
    CREATE ROLE store_owner NOLOGIN;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'store_app') THEN
    CREATE ROLE store_app NOLOGIN;
  END IF;
END $$;
CREATE SCHEMA store AUTHORIZATION store_owner;
SET LOCAL ROLE store_owner;
SET LOCAL search_path = store, pg_catalog;
\ir ../store.sql
\ir 01_bootstrap.sql
\ir 02_functions.sql
\ir 03_triggers.sql
\ir 04_procedures.sql
\ir 05_views.sql
\ir 06_indexes.sql
\ir 10_manager_profiles.sql
\ir 07_access.sql
COMMIT;

