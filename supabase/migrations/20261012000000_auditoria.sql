-- Auditoría de cambios: quién hizo qué y cuándo.
--
-- Decisión de roles: cualquier miembro del hogar puede crear, editar y borrar gastos, pagos, reembolsos,
-- aportaciones, perfiles y categorías (solo la gestión de miembros e invitaciones es de admin). Como
-- contrapartida, cada cambio queda registrado aquí y solo los admins pueden leerlo.
--
-- Escribe únicamente Core.Api (rol propietario), en la misma transacción que el cambio auditado. Es
-- append-only: ni siquiera el propietario puede modificar o borrar una fila, salvo el borrado en cascada
-- al eliminar el hogar.

create table public.auditoria (
    id          uuid primary key default gen_random_uuid(),
    hogar_id    uuid not null references public.hogar (id) on delete cascade,
    -- Sin clave foránea a auth.users: el rastro debe sobrevivir a que se borre la cuenta del autor.
    usuario_id  uuid,
    cuando      timestamptz not null default now(),
    accion      text not null check (accion in ('crear', 'editar', 'borrar', 'vincular', 'usar')),
    entidad     text not null check (length(entidad) between 1 and 40),
    entidad_id  uuid not null,
    -- Solo los campos que cambian en una edición; el estado completo al crear (despues) o borrar (antes).
    antes       jsonb,
    despues     jsonb,
    check (antes is not null or despues is not null)
);

create index auditoria_hogar_cuando_idx on public.auditoria (hogar_id, cuando desc);
create index auditoria_entidad_idx on public.auditoria (hogar_id, entidad, entidad_id);

alter table public.auditoria enable row level security;

-- Solo los admins del hogar leen el historial.
create policy auditoria_select on public.auditoria
    for select to authenticated
    using (public.es_admin_hogar(hogar_id));

revoke all on public.auditoria from anon, authenticated;
grant select on public.auditoria to authenticated;

create function public.auditoria_inmutable()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    -- El borrado en cascada desde hogar corre dentro de un trigger de integridad referencial
    -- (profundidad > 1); cualquier otro DELETE o UPDATE, aunque sea del propietario, se rechaza.
    if tg_op = 'DELETE' and pg_trigger_depth() > 1 then
        return old;
    end if;
    raise exception 'La auditoría es de solo añadir: no se puede modificar ni borrar';
end
$$;

revoke all on function public.auditoria_inmutable() from public, anon;

create trigger auditoria_inmutable
    before update or delete on public.auditoria
    for each row execute function public.auditoria_inmutable();

create function public.auditoria_sin_truncate()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    raise exception 'La auditoría es de solo añadir: no se puede vaciar';
end
$$;

revoke all on function public.auditoria_sin_truncate() from public, anon;

create trigger auditoria_sin_truncate
    before truncate on public.auditoria
    for each statement execute function public.auditoria_sin_truncate();
