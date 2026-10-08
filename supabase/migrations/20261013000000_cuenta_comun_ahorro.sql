-- Ahorro dentro de la cuenta común.
--
-- Cada aportación mensual se reparte en dos partes: 'ahorro' (apartada, los gastos no la tocan) y el resto
-- (importe - ahorro), que queda para gastos. Con ahorro = 0 todo se comporta como antes.
-- retirada_ahorro registra el dinero que el hogar saca de lo ahorrado (por ejemplo, para unas vacaciones):
-- baja el ahorro disponible, no el saldo de gastos. Core.Api impide retirar más de lo ahorrado.

alter table public.aportacion_cuenta
    add column ahorro numeric(12,2) not null default 0;

alter table public.aportacion_cuenta
    add constraint aportacion_cuenta_ahorro_valido check (ahorro >= 0 and ahorro <= importe);

create table public.retirada_ahorro (
    id         uuid primary key default gen_random_uuid(),
    hogar_id   uuid not null references public.hogar (id) on delete cascade,
    miembro_id uuid not null,
    fecha      date not null,
    importe    numeric(12,2) not null check (importe > 0),
    concepto   text check (length(concepto) <= 200),
    unique (hogar_id, id),
    foreign key (hogar_id, miembro_id) references public.miembro (hogar_id, id)
);

create index retirada_ahorro_fecha_idx on public.retirada_ahorro (hogar_id, fecha);

alter table public.retirada_ahorro enable row level security;

-- Solo lectura para los roles de cliente: toda escritura pasa por Core.Api (ver cerrar_escritura_directa).
create policy retirada_ahorro_select on public.retirada_ahorro
    for select to authenticated
    using (hogar_id in (select public.mis_hogares()));

revoke insert, update, delete, truncate, references, trigger on public.retirada_ahorro from anon, authenticated;
revoke select on public.retirada_ahorro from anon;
