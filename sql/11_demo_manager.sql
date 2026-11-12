-- Add a second manager once; never reset names edited in the application.
DO $$
DECLARE v_employee bigint; v_department bigint; v_role bigint;
BEGIN
 IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='store_manager_demo_2') THEN
  CREATE ROLE store_manager_demo_2 NOLOGIN IN ROLE store_app;
 END IF;
 IF NOT EXISTS (SELECT 1 FROM store.users WHERE username='store_manager_demo_2') THEN
  SELECT id INTO v_department FROM store.departments WHERE deleted_at IS NULL ORDER BY id LIMIT 1;
  SELECT id INTO v_role FROM store.roles WHERE name='manager';
  IF v_department IS NULL OR v_role IS NULL THEN
   RAISE EXCEPTION 'Install demo departments and roles before configuring managers';
  END IF;
  INSERT INTO store.employees(department_id,full_name,job_title,hired_on)
  VALUES(v_department,'Максим Бондар','Менеджер',current_date) RETURNING id INTO v_employee;
  INSERT INTO store.users(employee_id,role_id,username,password_hash)
  VALUES(v_employee,v_role,'store_manager_demo_2','!disabled:database-authentication-only!');
 END IF;
END $$;
