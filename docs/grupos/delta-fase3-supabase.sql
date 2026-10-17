-- =====================================================================================================================
-- Delta de la fase 3 de grupos para Supabase (proyecto de producción, que hoy tiene aplicadas las migraciones hasta la fase 2).
--
-- Cuándo: aplicar UNA vez tras fusionar la fase 3 en master, antes de desplegar la API y el front de la fase 3.
-- Cómo:   psql "<cadena de conexión de Supabase>" -v ON_ERROR_STOP=1 -f docs/grupos/delta-fase3-supabase.sql
--         Todo va en una sola transacción (BEGIN ... COMMIT): si algo falla, no queda nada a medias.
-- Orden:  1) columnas y restricciones nuevas de hogar (periodo, recurrentes_activas, liquidacion_modo);
--         2) tabla ciclo_liquidacion (restricciones, FKs, RLS, política y permisos);
--         3) columna gasto.ciclo_liquidacion_id y su FK compuesta;
--         4) función gasto_mes_abierto (reemplazo; cambia el disparador de gasto sin cambiar el disparador en sí);
--         5) política ciclo_liquidacion_select (drop + create).
-- Idempotente: cada sentencia comprueba antes si ya existe (add column if not exists, create table if not exists,
--         comprobación en pg_constraint para las restricciones y FKs, drop policy if exists, create or replace function,
--         revoke/grant repetibles). Puede ejecutarse dos veces sin error.
-- No es el SQL de migrar: no toca el journal miparte_migraciones ni reejecuta sentencias de la fase 1 o 2. Es la
--         diferencia exacta entre Estructura/ de la fase 2 (e4b88e3) y la de la fase 3, escrita a mano. Los scripts de
--         Scripts/ 20261020120000, 20261021120000 y 20261022120000 contienen lo mismo en forma de migración; este delta es
--         la versión para Supabase, sin journal.
-- Filas existentes: periodo_desde y periodo_hasta quedan a null (el hogar usa meses como hasta ahora), recurrentes_activas
--         a true (los hogares que ya generaban sus recurrentes siguen igual), liquidacion_modo a 'mensual' y los gastos
--         existentes con ciclo_liquidacion_id a null. No hay que rellenar datos.
-- =====================================================================================================================

begin;

-- 1) hogar: periodo propio, interruptor de recurrentes y modo de liquidación ---------------------------------------------

alter table public.hogar add column if not exists periodo_desde date;
alter table public.hogar add column if not exists periodo_hasta date;
alter table public.hogar add column if not exists recurrentes_activas boolean default true not null;
alter table public.hogar add column if not exists liquidacion_modo text default 'mensual'::text not null;

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'hogar_periodo_check' and conrelid = 'public.hogar'::regclass) then
        alter table public.hogar add constraint hogar_periodo_check check ((((periodo_desde is null) and (periodo_hasta is null)) or ((periodo_desde is not null) and ((periodo_hasta is null) or (periodo_hasta >= periodo_desde)))));
    end if;
end $$;

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'hogar_liquidacion_modo_check' and conrelid = 'public.hogar'::regclass) then
        alter table public.hogar add constraint hogar_liquidacion_modo_check check ((liquidacion_modo = any (array['mensual'::text, 'ciclos'::text])));
    end if;
end $$;

-- 2) ciclo_liquidacion: ciclos de liquidación cerrados ----------------------------------------------------------------

create table if not exists public.ciclo_liquidacion (
    id uuid default gen_random_uuid() not null,
    hogar_id uuid not null,
    numero integer not null,
    cerrado_en timestamp with time zone default now() not null,
    cerrado_por uuid not null,
    desde_gasto_fecha date not null,
    hasta_fecha date not null,
    total numeric(12,2) not null,
    constraint ciclo_liquidacion_fechas_check check ((desde_gasto_fecha <= hasta_fecha)),
    constraint ciclo_liquidacion_numero_check check ((numero > 0)),
    constraint ciclo_liquidacion_total_check check ((total > (0)::numeric))
);

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'ciclo_liquidacion_pkey' and conrelid = 'public.ciclo_liquidacion'::regclass) then
        alter table public.ciclo_liquidacion add constraint ciclo_liquidacion_pkey primary key (id);
    end if;
end $$;

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'ciclo_liquidacion_hogar_id_id_key' and conrelid = 'public.ciclo_liquidacion'::regclass) then
        alter table public.ciclo_liquidacion add constraint ciclo_liquidacion_hogar_id_id_key unique (hogar_id, id);
    end if;
end $$;

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'ciclo_liquidacion_hogar_id_numero_key' and conrelid = 'public.ciclo_liquidacion'::regclass) then
        alter table public.ciclo_liquidacion add constraint ciclo_liquidacion_hogar_id_numero_key unique (hogar_id, numero);
    end if;
end $$;

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'ciclo_liquidacion_hogar_id_fkey' and conrelid = 'public.ciclo_liquidacion'::regclass) then
        alter table public.ciclo_liquidacion add constraint ciclo_liquidacion_hogar_id_fkey foreign key (hogar_id) references public.hogar(id) on delete cascade;
    end if;
end $$;

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'ciclo_liquidacion_hogar_id_cerrado_por_fkey' and conrelid = 'public.ciclo_liquidacion'::regclass) then
        alter table public.ciclo_liquidacion add constraint ciclo_liquidacion_hogar_id_cerrado_por_fkey foreign key (hogar_id, cerrado_por) references public.miembro(hogar_id, id);
    end if;
end $$;

alter table public.ciclo_liquidacion enable row level security;

drop policy if exists ciclo_liquidacion_select on public.ciclo_liquidacion;
create policy ciclo_liquidacion_select on public.ciclo_liquidacion for select to authenticated using ((hogar_id in ( select public.mis_hogares() as mis_hogares)));

revoke all on table public.ciclo_liquidacion from anon, authenticated;
grant select on table public.ciclo_liquidacion to authenticated;

-- 3) gasto: marca de liquidado por ciclo -------------------------------------------------------------------------------

alter table public.gasto add column if not exists ciclo_liquidacion_id uuid;

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'gasto_hogar_id_ciclo_liquidacion_id_fkey' and conrelid = 'public.gasto'::regclass) then
        alter table public.gasto add constraint gasto_hogar_id_ciclo_liquidacion_id_fkey foreign key (hogar_id, ciclo_liquidacion_id) references public.ciclo_liquidacion(hogar_id, id);
    end if;
end $$;

-- 4) gasto_mes_abierto: reasignar categoría, perfil o ciclo no cambia importes ni reparto, así que se permite con el mes cerrado

create or replace function public.gasto_mes_abierto() returns trigger
    language plpgsql security definer
    set search_path to ''
    as $$
begin
    if tg_op = 'DELETE' then
        perform public.exigir_mes_abierto(old.hogar_id, array[old.fecha]);
        return old;
    elsif tg_op = 'INSERT' then
        perform public.exigir_mes_abierto(new.hogar_id, array[new.fecha]);
    else
        -- Reasignar la categoría o el perfil (al borrar una de ellas en uso) no toca importes ni liquidación, solo la estadística:
        -- se permite aunque el mes esté cerrado. Marcar el gasto como liquidado por un ciclo (ciclo_liquidacion_id) tampoco cambia
        -- importes ni reparto: solo registra que ya está saldado.
        if (to_jsonb(new) - 'categoria_id' - 'perfil_reparto_id' - 'ciclo_liquidacion_id')
            = (to_jsonb(old) - 'categoria_id' - 'perfil_reparto_id' - 'ciclo_liquidacion_id') then
            return new;
        end if;
        perform public.exigir_mes_abierto(new.hogar_id, array[old.fecha, new.fecha]);
    end if;
    return new;
end;
$$;

revoke all on function public.gasto_mes_abierto() from public;

commit;
