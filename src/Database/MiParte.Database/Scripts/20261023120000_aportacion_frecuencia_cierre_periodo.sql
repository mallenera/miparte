-- Frecuencia de las aportaciones a la cuenta común y cierre del periodo propio (fase 3 de grupos).
-- aportacion_frecuencia: unica, mensual (la de todos los hogares existentes, sin cambios), bimestral, trimestral, semestral o anual.
-- periodo_cerrado_en: marca de cierre de un viaje o evento; cerrado, los gastos de su rango no se pueden editar ni borrar.
-- La estructura ya lo añade (Estructura/01_Tablas/hogar.sql); este script lo deja explícito y aplicable por sí solo en bases ya
-- migradas. No necesita ir antes de la estructura: las filas existentes quedan en 'mensual' y sin cierre. Idempotente.
alter table public.hogar add column if not exists aportacion_frecuencia text not null default 'mensual';
alter table public.hogar add column if not exists periodo_cerrado_en timestamptz;

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'hogar_aportacion_frecuencia_check' and conrelid = 'public.hogar'::regclass) then
        alter table public.hogar add constraint hogar_aportacion_frecuencia_check
            check (aportacion_frecuencia in ('unica', 'mensual', 'bimestral', 'trimestral', 'semestral', 'anual'));
    end if;
end $$;

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'hogar_periodo_cerrado_check' and conrelid = 'public.hogar'::regclass) then
        alter table public.hogar add constraint hogar_periodo_cerrado_check
            check (periodo_cerrado_en is null or (periodo_desde is not null and periodo_hasta is not null));
    end if;
end $$;
