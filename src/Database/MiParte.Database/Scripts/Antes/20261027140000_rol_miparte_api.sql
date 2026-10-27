-- Rol de conexión de Core.Api: mínimo privilegio. Hasta ahora la API entraba como postgres, propietario de las tablas:
-- se saltaba la RLS y podía hacer DDL, leer auth.users o vaciar la auditoría. miparte_api solo lee y escribe filas de
-- public (permisos y política en Estructura/05_Seguridad); postgres queda para migrar.
-- Antes de la estructura, que ya concede permisos a este rol. Los roles son del servidor, no de la base: si ya existe
-- (otra base del mismo servidor, o alguien lo creó a mano) se reutiliza, pero se rechaza si tiene atributos peligrosos.
-- Sin contraseña: se fija aparte, nunca en el repositorio (docs/despliegue.md). Idempotente.
do $$
begin
    if not exists (select 1 from pg_catalog.pg_roles where rolname = 'miparte_api') then
        begin
            create role miparte_api login nosuperuser nocreatedb nocreaterole noinherit noreplication nobypassrls;
        exception when duplicate_object or unique_violation then
            null; -- otra base del mismo servidor lo ha creado a la vez (tests en paralelo)
        end;
    end if;

    if exists (select 1 from pg_catalog.pg_roles where rolname = 'miparte_api'
               and (rolsuper or rolcreatedb or rolcreaterole or rolreplication or rolbypassrls)) then
        raise exception 'El rol miparte_api tiene atributos de administración (superuser, createdb, createrole, replication o bypassrls): quítaselos antes de migrar';
    end if;
    -- Pertenecer a otro rol (p. ej. postgres) le dejaría hacer SET ROLE y recuperar todos sus privilegios.
    if exists (select 1 from pg_catalog.pg_auth_members am join pg_catalog.pg_roles r on r.oid = am.member
               where r.rolname = 'miparte_api') then
        raise exception 'El rol miparte_api pertenece a otro rol: revoca esa pertenencia antes de migrar';
    end if;
end
$$;
