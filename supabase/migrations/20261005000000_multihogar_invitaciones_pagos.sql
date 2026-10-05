-- Mi Parte, Tu Parte: multihogar, invitaciones, pagos de liquidación y roles.
--
-- Contenido:
--  1. miembro.rol ('admin' | 'miembro') y funciones auxiliares de rol.
--  2. mis_hogares() solo devuelve hogares donde el miembro está activo.
--  3. RLS de miembro endurecida (políticas separadas + trigger de protección).
--  4. invitacion_hogar + aceptar_invitacion(p_token, p_nombre).
--  5. pago_liquidacion (transferencias que saldan la liquidación mensual).
--  6. crear_hogar: creador como admin + perfiles de reparto y categorías por defecto.
--
-- Un hogar con un único adulto es válido: no se impone ningún mínimo de miembros.

-- ---------------------------------------------------------------------------
-- 1. Rol del miembro
-- ---------------------------------------------------------------------------

alter table public.miembro
    add column rol text not null default 'miembro' check (rol in ('admin', 'miembro'));

-- Hogares ya existentes (creados con la migración inicial) no tienen admin:
-- se promociona a un miembro vinculado por hogar, de forma determinista.
update public.miembro m
   set rol = 'admin'
  from (
        select distinct on (hogar_id) id
          from public.miembro
         where user_id is not null and activo
         order by hogar_id, id
       ) primero
 where m.id = primero.id
   and not exists (
        select 1 from public.miembro a
         where a.hogar_id = m.hogar_id and a.rol = 'admin'
   );

-- ¿Es el usuario autenticado admin activo de ese hogar? SECURITY DEFINER para
-- poder usarla dentro de políticas de miembro sin recursión.
create function public.es_admin_hogar(p_hogar uuid)
returns boolean
language sql
stable
security definer
set search_path = ''
as $$
    select exists (
        select 1 from public.miembro m
         where m.hogar_id = p_hogar
           and m.user_id = auth.uid()
           and m.rol = 'admin'
           and m.activo
    );
$$;

revoke all on function public.es_admin_hogar(uuid) from public, anon;
grant execute on function public.es_admin_hogar(uuid) to authenticated;

-- ¿Queda en el hogar algún admin activo y vinculado distinto de p_excluir?
-- Se usa para impedir que un hogar se quede sin administrador.
create function public.hogar_tiene_otro_admin(p_hogar uuid, p_excluir uuid)
returns boolean
language sql
stable
security definer
set search_path = ''
as $$
    select exists (
        select 1 from public.miembro m
         where m.hogar_id = p_hogar
           and m.id <> p_excluir
           and m.rol = 'admin'
           and m.activo
           and m.user_id is not null
    );
$$;

revoke all on function public.hogar_tiene_otro_admin(uuid, uuid) from public, anon;
grant execute on function public.hogar_tiene_otro_admin(uuid, uuid) to authenticated;

-- ---------------------------------------------------------------------------
-- 2. mis_hogares(): solo membresías activas
-- ---------------------------------------------------------------------------
-- Un miembro desactivado (activo = false) deja de tener acceso al hogar vía RLS.

create or replace function public.mis_hogares()
returns setof uuid
language sql
stable
security definer
set search_path = ''
as $$
    select m.hogar_id
      from public.miembro m
     where m.user_id = auth.uid()
       and m.activo = true;
$$;

-- ---------------------------------------------------------------------------
-- 3. RLS endurecida de miembro
-- ---------------------------------------------------------------------------
-- Decisión: la política genérica "for all" permitía a cualquier miembro editar a
-- cualquiera (cambiar user_id, auto-promocionarse...). Se sustituye por:
--   * select : cualquier miembro activo del hogar.
--   * insert : solo admin del hogar (alta de personas sin cuenta, p. ej. hijos).
--   * update : admin del hogar, o el propio miembro sobre su fila.
--   * delete : solo admin del hogar.
-- Las políticas RLS no pueden comparar valor antiguo y nuevo, así que lo que cada
-- rol puede cambiar en un UPDATE lo impone el trigger miembro_proteger, que
-- solo actúa para los roles de cliente (authenticated/anon). Las funciones
-- SECURITY DEFINER (aceptar_invitacion, crear_hogar) y el rol de servicio de
-- Core.Api se ejecutan con otro current_user y no se ven afectados; no se usa
-- ningún flag configurable por el cliente, que sería falsificable.
-- Reglas del trigger:
--   * user_id nunca lo fija un cliente: se vincula solo con aceptar_invitacion
--     (un admin sí puede desvincular poniéndolo a NULL).
--   * hogar_id es inmutable.
--   * un no-admin solo puede cambiar su nombre (no rol, activo, tipo ni responsable).
--   * no se puede dejar al hogar sin admin activo vinculado (degradar, desactivar,
--     desvincular o borrar al último).

drop policy miembro_hogar on public.miembro;

create policy miembro_select on public.miembro
    for select to authenticated
    using (hogar_id in (select public.mis_hogares()));

create policy miembro_insert on public.miembro
    for insert to authenticated
    with check (public.es_admin_hogar(hogar_id));

create policy miembro_update on public.miembro
    for update to authenticated
    using (public.es_admin_hogar(hogar_id)
           or (user_id = auth.uid() and hogar_id in (select public.mis_hogares())))
    with check (public.es_admin_hogar(hogar_id)
           or (user_id = auth.uid() and hogar_id in (select public.mis_hogares())));

create policy miembro_delete on public.miembro
    for delete to authenticated
    using (public.es_admin_hogar(hogar_id));

create function public.miembro_proteger()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    -- Solo se restringe a los clientes con JWT; servicio y funciones definer pasan.
    if current_user not in ('authenticated', 'anon') then
        if tg_op = 'DELETE' then return old; end if;
        return new;
    end if;

    if tg_op = 'INSERT' then
        if new.user_id is not null then
            raise exception 'El usuario de un miembro solo se vincula mediante una invitación';
        end if;
        return new;
    end if;

    if tg_op = 'DELETE' then
        if old.rol = 'admin' and old.activo and old.user_id is not null
           and not public.hogar_tiene_otro_admin(old.hogar_id, old.id) then
            raise exception 'El hogar debe conservar al menos un administrador';
        end if;
        return old;
    end if;

    -- UPDATE
    if new.hogar_id <> old.hogar_id then
        raise exception 'No se puede mover un miembro a otro hogar';
    end if;

    if new.user_id is distinct from old.user_id
       and not (new.user_id is null and public.es_admin_hogar(old.hogar_id)) then
        raise exception 'El usuario de un miembro solo se vincula mediante una invitación';
    end if;

    if not public.es_admin_hogar(old.hogar_id) then
        if new.rol is distinct from old.rol
           or new.activo is distinct from old.activo
           or new.tipo is distinct from old.tipo
           or new.responsable_id is distinct from old.responsable_id then
            raise exception 'Solo un administrador puede cambiar el rol o el estado de un miembro';
        end if;
    end if;

    if old.rol = 'admin' and old.activo and old.user_id is not null
       and (new.rol <> 'admin' or not new.activo or new.user_id is null)
       and not public.hogar_tiene_otro_admin(old.hogar_id, old.id) then
        raise exception 'El hogar debe conservar al menos un administrador';
    end if;

    return new;
end
$$;

revoke all on function public.miembro_proteger() from public, anon;

create trigger miembro_proteger
    before insert or update or delete on public.miembro
    for each row execute function public.miembro_proteger();

-- ---------------------------------------------------------------------------
-- 4. Invitaciones al hogar
-- ---------------------------------------------------------------------------
-- Se guarda solo el hash del token: SHA-256 en hexadecimal minúscula del token
-- en UTF-8. El token en claro se genera aleatorio en el servicio, se entrega
-- una única vez al invitado y no se persiste.
-- Si miembro_id es NULL, quien acepta entra como miembro adulto nuevo.

create table public.invitacion_hogar (
    id          uuid primary key default gen_random_uuid(),
    hogar_id    uuid not null references public.hogar (id) on delete cascade,
    miembro_id  uuid,
    token_hash  text not null unique check (token_hash ~ '^[0-9a-f]{64}$'),
    creada_por  uuid not null default auth.uid() references auth.users (id) on delete cascade,
    creada_en   timestamptz not null default now(),
    caduca_en   timestamptz not null,
    usada_en    timestamptz,
    usada_por   uuid references auth.users (id) on delete set null,
    unique (hogar_id, id),
    foreign key (hogar_id, miembro_id) references public.miembro (hogar_id, id) on delete cascade,
    check (caduca_en > creada_en),
    check ((usada_en is null) = (usada_por is null))
);

create index invitacion_hogar_idx on public.invitacion_hogar (hogar_id);

alter table public.invitacion_hogar enable row level security;

-- Solo los admins del hogar gestionan invitaciones. Quien acepta no necesita
-- acceso a la tabla: lo hace con aceptar_invitacion (SECURITY DEFINER).
create policy invitacion_hogar_select on public.invitacion_hogar
    for select to authenticated
    using (public.es_admin_hogar(hogar_id));

create policy invitacion_hogar_insert on public.invitacion_hogar
    for insert to authenticated
    with check (public.es_admin_hogar(hogar_id) and creada_por = auth.uid());

create policy invitacion_hogar_update on public.invitacion_hogar
    for update to authenticated
    using (public.es_admin_hogar(hogar_id))
    with check (public.es_admin_hogar(hogar_id));

create policy invitacion_hogar_delete on public.invitacion_hogar
    for delete to authenticated
    using (public.es_admin_hogar(hogar_id));

-- Acepta una invitación: vincula al usuario autenticado con el miembro destino,
-- o, si la invitación no tiene miembro destino, crea un miembro adulto con
-- p_nombre. Devuelve el hogar_id.
create function public.aceptar_invitacion(p_token text, p_nombre text default null)
returns uuid
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_user uuid := auth.uid();
    v_inv  public.invitacion_hogar;
    v_miembro public.miembro;
begin
    if v_user is null then
        raise exception 'Usuario no autenticado';
    end if;
    if p_token is null or length(p_token) = 0 then
        raise exception 'Invitación no válida';
    end if;

    -- Bloqueo de fila: dos aceptaciones simultáneas no pueden usar el mismo token.
    select * into v_inv
      from public.invitacion_hogar i
     where i.token_hash = encode(sha256(convert_to(p_token, 'UTF8')), 'hex')
     for update;

    if not found then
        raise exception 'Invitación no válida';
    end if;
    if v_inv.usada_en is not null then
        raise exception 'La invitación ya fue utilizada';
    end if;
    if v_inv.caduca_en <= now() then
        raise exception 'La invitación ha caducado';
    end if;

    -- Un usuario no puede figurar dos veces en el mismo hogar (activo o no).
    if exists (select 1 from public.miembro m
                where m.hogar_id = v_inv.hogar_id and m.user_id = v_user) then
        raise exception 'Ya perteneces a este hogar';
    end if;

    if v_inv.miembro_id is not null then
        select * into v_miembro
          from public.miembro m
         where m.hogar_id = v_inv.hogar_id and m.id = v_inv.miembro_id
         for update;

        if not found or not v_miembro.activo then
            raise exception 'El miembro de la invitación ya no está disponible';
        end if;
        if v_miembro.user_id is not null then
            raise exception 'El miembro de la invitación ya está vinculado a un usuario';
        end if;

        update public.miembro set user_id = v_user where id = v_miembro.id;
    else
        if p_nombre is null or length(btrim(p_nombre)) = 0 then
            raise exception 'Hay que indicar un nombre para unirse al hogar';
        end if;

        insert into public.miembro (hogar_id, nombre, tipo, user_id, rol)
        values (v_inv.hogar_id, btrim(p_nombre), 'adulto', v_user, 'miembro');
    end if;

    update public.invitacion_hogar
       set usada_en = now(), usada_por = v_user
     where id = v_inv.id;

    return v_inv.hogar_id;
end
$$;

revoke all on function public.aceptar_invitacion(text, text) from public, anon;
grant execute on function public.aceptar_invitacion(text, text) to authenticated;

-- ---------------------------------------------------------------------------
-- 5. Pagos de liquidación
-- ---------------------------------------------------------------------------
-- Transferencias reales entre miembros para saldar la liquidación de un mes.
-- mes es siempre el primer día del mes.

create table public.pago_liquidacion (
    id            uuid primary key default gen_random_uuid(),
    hogar_id      uuid not null references public.hogar (id) on delete cascade,
    mes           date not null check (extract(day from mes) = 1),
    de_miembro_id uuid not null,
    a_miembro_id  uuid not null,
    importe       numeric(12,2) not null check (importe > 0),
    fecha         date not null,
    concepto      text,
    unique (hogar_id, id),
    foreign key (hogar_id, de_miembro_id) references public.miembro (hogar_id, id),
    foreign key (hogar_id, a_miembro_id)  references public.miembro (hogar_id, id),
    check (de_miembro_id <> a_miembro_id)
);

create index pago_liquidacion_mes_idx on public.pago_liquidacion (hogar_id, mes);

alter table public.pago_liquidacion enable row level security;

create policy pago_liquidacion_hogar on public.pago_liquidacion
    for all to authenticated
    using (hogar_id in (select public.mis_hogares()))
    with check (hogar_id in (select public.mis_hogares()));

-- ---------------------------------------------------------------------------
-- 6. crear_hogar: creador admin + datos por defecto
-- ---------------------------------------------------------------------------
-- Perfiles: Proporcional a ingresos (ingresos), Por partes (partes), Porcentaje
-- fijo (porcentaje) e Individual (individual). Para que un hogar de un solo
-- adulto funcione desde el principio, el creador recibe 1 parte en "Por partes"
-- y el 100 % en "Porcentaje fijo"; al añadir miembros hay que reajustar ambos.
-- Categorías de ejemplo, cada una con su perfil por defecto.

create or replace function public.crear_hogar(p_nombre_hogar text, p_nombre_miembro text)
returns uuid
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_hogar    uuid;
    v_miembro  uuid;
    v_ingresos uuid;
    v_partes   uuid;
    v_porcent  uuid;
    v_indiv    uuid;
begin
    if auth.uid() is null then
        raise exception 'Usuario no autenticado';
    end if;

    insert into public.hogar (nombre) values (p_nombre_hogar) returning id into v_hogar;
    insert into public.miembro (hogar_id, nombre, tipo, user_id, rol)
    values (v_hogar, p_nombre_miembro, 'adulto', auth.uid(), 'admin')
    returning id into v_miembro;

    insert into public.perfil_reparto (hogar_id, nombre, modo)
    values (v_hogar, 'Proporcional a ingresos', 'ingresos') returning id into v_ingresos;
    insert into public.perfil_reparto (hogar_id, nombre, modo)
    values (v_hogar, 'Por partes', 'partes') returning id into v_partes;
    insert into public.perfil_reparto (hogar_id, nombre, modo)
    values (v_hogar, 'Porcentaje fijo', 'porcentaje') returning id into v_porcent;
    insert into public.perfil_reparto (hogar_id, nombre, modo)
    values (v_hogar, 'Individual', 'individual') returning id into v_indiv;

    insert into public.perfil_reparto_detalle (hogar_id, perfil_id, miembro_id, valor) values
        (v_hogar, v_partes,  v_miembro, 1),
        (v_hogar, v_porcent, v_miembro, 100);

    insert into public.categoria (hogar_id, nombre, perfil_reparto_id) values
        (v_hogar, 'Hipoteca/Alquiler',       v_ingresos),
        (v_hogar, 'Alimentación',            v_ingresos),
        (v_hogar, 'Suministros',             v_ingresos),
        (v_hogar, 'Gastos varios de casa',   v_ingresos),
        (v_hogar, 'Hijo',                    v_partes),
        (v_hogar, 'Ocio personal',           v_indiv);

    return v_hogar;
end
$$;

revoke all on function public.crear_hogar(text, text) from public, anon;
grant execute on function public.crear_hogar(text, text) to authenticated;
