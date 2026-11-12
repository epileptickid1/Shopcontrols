REVOKE ALL ON SCHEMA store FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA store FROM PUBLIC,store_app;
REVOKE ALL ON ALL SEQUENCES IN SCHEMA store FROM PUBLIC,store_app;
REVOKE ALL ON ALL ROUTINES IN SCHEMA store FROM PUBLIC,store_app;
GRANT USAGE ON SCHEMA store TO store_app;
ALTER DEFAULT PRIVILEGES FOR ROLE store_owner IN SCHEMA store REVOKE EXECUTE ON ROUTINES FROM PUBLIC;
DO $$
DECLARE r record;
BEGIN
 FOR r IN SELECT tablename FROM pg_tables WHERE schemaname='store' LOOP
  -- The app intentionally receives no direct table rights.
  NULL;
 END LOOP;
 FOR r IN SELECT viewname FROM pg_views WHERE schemaname='store' LOOP
  EXECUTE format('GRANT SELECT ON store.%I TO store_app',r.viewname);
 END LOOP;
 FOR r IN SELECT p.oid::regprocedure AS signature FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
 WHERE n.nspname='store' AND p.prokind='p' LOOP
  EXECUTE format('GRANT EXECUTE ON PROCEDURE %s TO store_app',r.signature);
 END LOOP;
END $$;
-- Needed by views; helpers and trigger functions remain private.
GRANT EXECUTE ON FUNCTION store.fn_sale_total(bigint),store.fn_supply_total(bigint) TO store_app;

