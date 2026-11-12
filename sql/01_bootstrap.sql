-- Bootstrap before audit triggers exist. Installer becomes the system actor.
INSERT INTO store.roles (name, description) VALUES
 ('system', 'Initial database maintenance account'),
 ('manager', 'Catalog, supply and sales operations'),
 ('cashier', 'Sales operations');
INSERT INTO store.users (role_id, username, password_hash)
SELECT id, session_user, '!disabled:database-authentication-only!'
FROM store.roles WHERE name = 'system';
UPDATE store.users SET updated_by = id;
ALTER TABLE store.users ALTER COLUMN updated_by SET NOT NULL;
ALTER TABLE store.users ADD CONSTRAINT ck_users_system_employee
 CHECK (employee_id IS NOT NULL OR id = 1);
-- ID 1 is created in a fresh database; bootstrap is not a migration.
-- No app passwords: the sentinel disables application password login.

