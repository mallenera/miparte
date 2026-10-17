-- Periodo propio del hogar (fase 3 de grupos): desde y hasta, ambos opcionales. Null = el hogar usa meses como hasta ahora.
-- La estructura ya lo añade (Estructura/01_Tablas/hogar.sql); este script lo deja explícito y aplicable por sí solo en
-- bases ya migradas. No necesita ir antes de la estructura ni rellenar datos: las filas existentes quedan en null, que
-- cumple la restricción. Idempotente.
alter table public.hogar add column if not exists periodo_desde date;
alter table public.hogar add column if not exists periodo_hasta date;

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'hogar_periodo_check' and conrelid = 'public.hogar'::regclass) then
        alter table public.hogar add constraint hogar_periodo_check check (
            ((periodo_desde is null) and (periodo_hasta is null))
            or (periodo_desde is not null and (periodo_hasta is null or periodo_hasta >= periodo_desde))
        );
    end if;
end $$;
