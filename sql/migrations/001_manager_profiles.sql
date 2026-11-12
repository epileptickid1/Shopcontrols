\set ON_ERROR_STOP on
BEGIN;
SET LOCAL ROLE store_owner;
\ir ../10_manager_profiles.sql
REVOKE ALL ON PROCEDURE store.sp_rename_current_manager(text) FROM PUBLIC;
GRANT EXECUTE ON PROCEDURE store.sp_rename_current_manager(text) TO store_app;
GRANT SELECT ON store.v_manager_profiles TO store_app;
RESET ROLE;
\ir ../11_demo_manager.sql
COMMIT;
