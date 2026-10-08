-- Simula lo mínimo del esquema auth de Supabase para probar las migraciones
-- en un PostgreSQL normal (CI y desarrollo local). NO aplicar en Supabase.
do $$
begin
    if not exists (select from pg_roles where rolname = 'anon') then create role anon nologin; end if;
    if not exists (select from pg_roles where rolname = 'authenticated') then create role authenticated nologin; end if;
end
$$;

create schema if not exists auth;
create table if not exists auth.users (id uuid primary key);
create or replace function auth.uid() returns uuid language sql stable as
$$ select nullif(current_setting('request.jwt.claim.sub', true), '')::uuid $$;
grant usage on schema auth to authenticated, anon;
grant execute on function auth.uid() to authenticated, anon;

-- Supabase concede por defecto todos los privilegios sobre las tablas de public a anon y authenticated
-- (a ellos los acota luego la RLS). Se imita para que los revoke de las migraciones tengan algo que quitar
-- y los tests de permisos sean significativos.
grant usage on schema public to anon, authenticated;
alter default privileges in schema public grant all on tables to anon, authenticated;
