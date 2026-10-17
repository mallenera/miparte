-- Liquidación por ciclos (fase 3 de grupos): modo de liquidación del hogar, tabla de ciclos cerrados y marca de liquidado en
-- el gasto. La estructura ya lo añade (Estructura/01_Tablas, 04_ClavesForaneas y 05_Seguridad); este script lo deja explícito y
-- aplicable por sí solo en bases ya migradas. No necesita ir antes de la estructura: las filas existentes quedan en 'mensual' y
-- sin ciclo, que es el comportamiento de siempre. Idempotente.
alter table public.hogar add column if not exists liquidacion_modo text not null default 'mensual';

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'hogar_liquidacion_modo_check' and conrelid = 'public.hogar'::regclass) then
        alter table public.hogar add constraint hogar_liquidacion_modo_check
            check (liquidacion_modo in ('mensual', 'ciclos'));
    end if;
end $$;

create table if not exists public.ciclo_liquidacion (
    id uuid not null default gen_random_uuid() primary key,
    hogar_id uuid not null references public.hogar(id) on delete cascade,
    numero integer not null check (numero > 0),
    cerrado_en timestamptz not null default now(),
    cerrado_por uuid not null,
    desde_gasto_fecha date not null,
    hasta_fecha date not null,
    total numeric(12,2) not null check (total > 0),
    constraint ciclo_liquidacion_hogar_id_id_key unique (hogar_id, id),
    constraint ciclo_liquidacion_hogar_id_numero_key unique (hogar_id, numero),
    constraint ciclo_liquidacion_fechas_check check (desde_gasto_fecha <= hasta_fecha)
);

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'ciclo_liquidacion_hogar_id_cerrado_por_fkey' and conrelid = 'public.ciclo_liquidacion'::regclass) then
        alter table public.ciclo_liquidacion add constraint ciclo_liquidacion_hogar_id_cerrado_por_fkey
            foreign key (hogar_id, cerrado_por) references public.miembro(hogar_id, id);
    end if;
end $$;

alter table public.ciclo_liquidacion enable row level security;
drop policy if exists ciclo_liquidacion_select on public.ciclo_liquidacion;
create policy ciclo_liquidacion_select on public.ciclo_liquidacion for select to authenticated
    using (hogar_id in (select public.mis_hogares()));
revoke all on table public.ciclo_liquidacion from anon, authenticated;
grant select on table public.ciclo_liquidacion to authenticated;

alter table public.gasto add column if not exists ciclo_liquidacion_id uuid;

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'gasto_hogar_id_ciclo_liquidacion_id_fkey' and conrelid = 'public.gasto'::regclass) then
        alter table public.gasto add constraint gasto_hogar_id_ciclo_liquidacion_id_fkey
            foreign key (hogar_id, ciclo_liquidacion_id) references public.ciclo_liquidacion(hogar_id, id);
    end if;
end $$;
