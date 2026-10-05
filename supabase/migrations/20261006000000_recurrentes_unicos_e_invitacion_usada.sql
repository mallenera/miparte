-- Migración: unicidad de gastos recurrentes por mes y restricción de invitación usada.
--
--  1. Un gasto recurrente solo puede generarse una vez por mes y hogar. Sin esta
--     garantía, dos peticiones simultáneas a "generar" duplican los gastos.
--  2. invitacion_hogar: la restricción original impedía borrar de auth.users a
--     quien aceptó una invitación.

-- ---------------------------------------------------------------------------
-- 1. Un gasto por plantilla y mes
-- ---------------------------------------------------------------------------
-- Duplicados previos: lo normal es que no existan (tablas recientes, sin datos
-- reales), pero si los hubiera el índice no se podría crear. Se conserva el de
-- menor id por plantilla y mes y se borran los demás (sus filas de gasto_reparto
-- caen por ON DELETE CASCADE).
delete from public.gasto g
using (
    select id,
           row_number() over (
               partition by hogar_id, gasto_recurrente_id, date_trunc('month', fecha::timestamp)
               order by id
           ) as n
      from public.gasto
     where gasto_recurrente_id is not null
) d
where g.id = d.id and d.n > 1;

-- La expresión debe ser IMMUTABLE para poder indexarse: date_trunc sobre
-- timestamp (sin zona) lo es; sobre date/timestamptz depende de la zona horaria.
create unique index gasto_recurrente_mes_uq
    on public.gasto (hogar_id, gasto_recurrente_id, (date_trunc('month', fecha::timestamp)::date))
    where gasto_recurrente_id is not null;

-- ---------------------------------------------------------------------------
-- 2. invitacion_hogar: usada_por puede quedar nulo al borrar al usuario
-- ---------------------------------------------------------------------------
-- La restricción original, check ((usada_en is null) = (usada_por is null)), choca
-- con "usada_por ... on delete set null": al borrar al usuario aceptante usada_por
-- pasa a NULL con usada_en no nulo y el DELETE en auth.users fallaba. La regla
-- correcta es de un solo sentido: si hay usada_por, tiene que haber usada_en.
-- Como el check es anónimo (nombre autogenerado), se localiza por su definición.
do $$
declare
    v_nombre text;
begin
    select c.conname into v_nombre
      from pg_constraint c
     where c.conrelid = 'public.invitacion_hogar'::regclass
       and c.contype = 'c'
       and regexp_replace(pg_get_constraintdef(c.oid), '[\s()]', '', 'g')
           = 'CHECKusada_enISNULL=usada_porISNULL';

    if v_nombre is null then
        raise exception 'No se encontró el check (usada_en is null) = (usada_por is null) en invitacion_hogar';
    end if;

    execute format('alter table public.invitacion_hogar drop constraint %I', v_nombre);
end $$;

alter table public.invitacion_hogar
    add constraint invitacion_hogar_usada_coherente
    check (usada_por is null or usada_en is not null);
