-- Tipo de grupo: hogar.tipo ('hogar' por defecto; piso_compartido, viaje, evento u otro). La estructura ya lo añade
-- (Estructura/01_Tablas/hogar.sql); este script lo deja explícito y aplicable por sí solo. Idempotente.
alter table public.hogar add column if not exists tipo text not null default 'hogar';

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'hogar_tipo_check' and conrelid = 'public.hogar'::regclass) then
        alter table public.hogar add constraint hogar_tipo_check
            check (tipo in ('hogar', 'piso_compartido', 'viaje', 'evento', 'otro'));
    end if;
end $$;
