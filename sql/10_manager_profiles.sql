-- Shared by fresh installation and the idempotent manager-profile migration.
CREATE OR REPLACE VIEW store.v_manager_profiles AS
 SELECT u.id,u.username,e.full_name,r.name AS role_name,e.updated_at,e.updated_by
 FROM store.users u JOIN store.roles r ON r.id=u.role_id
 JOIN store.employees e ON e.id=u.employee_id
 JOIN store.departments d ON d.id=e.department_id
 WHERE r.name='manager' AND u.deleted_at IS NULL
 AND e.deleted_at IS NULL AND d.deleted_at IS NULL;

CREATE OR REPLACE PROCEDURE store.sp_rename_current_manager(p_full_name text)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
DECLARE v_actor bigint; v_name text;
BEGIN
 v_actor := store.fn_actor_id(ARRAY['manager']);
 v_name := regexp_replace(p_full_name,'^[[:space:]]+|[[:space:]]+$','','g');
 IF v_name IS NULL OR length(v_name) NOT BETWEEN 1 AND 200 THEN
  RAISE EXCEPTION 'Manager name must contain 1 to 200 characters' USING ERRCODE='23514';
 END IF;
 -- The target comes only from SESSION_USER; clients cannot rename another employee.
 UPDATE store.employees SET full_name=v_name
 WHERE id=(SELECT employee_id FROM store.users WHERE id=v_actor) AND deleted_at IS NULL;
 IF NOT FOUND THEN RAISE EXCEPTION 'Active manager employee not found' USING ERRCODE='42501'; END IF;
END $$;
