-- Cierre de mes.
--
-- Un mes cerrado congela sus gastos (y con ellos su reparto, su resumen y su liquidación): no se pueden crear,
-- editar ni borrar gastos con fecha en ese mes, ni generar recurrentes en él. Solo un admin cierra o reabre
-- (lo comprueba Core.Api). Los pagos de liquidación siguen permitidos: saldan lo congelado, no lo cambian.
-- mes es siempre el primer día del mes. Reabrir = borrar la fila.

create table public.mes_cerrado (
    id         uuid primary key default gen_random_uuid(),
    hogar_id   uuid not null references public.hogar (id) on delete cascade,
    mes        date not null check (extract(day from mes) = 1),
    cerrado_en timestamptz not null default now(),
    cerrado_por uuid,
    unique (hogar_id, id),
    unique (hogar_id, mes)
);

alter table public.mes_cerrado enable row level security;

-- Solo lectura para los roles de cliente: toda escritura pasa por Core.Api (ver cerrar_escritura_directa).
create policy mes_cerrado_select on public.mes_cerrado
    for select to authenticated
    using (hogar_id in (select public.mis_hogares()));

revoke insert, update, delete, truncate, references, trigger on public.mes_cerrado from anon, authenticated;
revoke select on public.mes_cerrado from anon;
