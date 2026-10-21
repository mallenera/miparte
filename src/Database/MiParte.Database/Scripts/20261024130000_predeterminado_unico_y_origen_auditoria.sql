-- Dos cambios del punto 18 (alta de gastos por chat), acordados con la pareja:
-- 1) Como máximo un perfil de reparto y una categoría predeterminados para gastos por hogar: índices únicos parciales.
--    Si ya hubiera dos marcados en un hogar, la creación del índice falla a propósito: no se desmarca nada en silencio.
-- 2) Origen de cada evento de auditoría: 'manual' (por defecto, lo que ya existía) o 'asistente' (alta confirmada desde el chat).
-- La estructura ya lo añade (Estructura/01_Tablas); este script lo deja explícito para bases ya migradas. Idempotente.

alter table public.auditoria add column if not exists origen text not null default 'manual';

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'auditoria_origen_check' and conrelid = 'public.auditoria'::regclass) then
        alter table public.auditoria add constraint auditoria_origen_check
            check (origen in ('manual', 'asistente'));
    end if;
end $$;

create unique index if not exists perfil_reparto_predeterminado_gastos_key
    on public.perfil_reparto (hogar_id) where predeterminado_gastos;
create unique index if not exists categoria_predeterminado_gastos_key
    on public.categoria (hogar_id) where predeterminado_gastos;
