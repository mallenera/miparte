-- Depósitos puntuales en el ahorro de la cuenta común.
--
-- Dinero que entra al ahorro fuera de la aportación mensual: el ahorro inicial al empezar a usar la app,
-- un premio de lotería, un regalo... Suma al ahorro disponible (como lo ahorrado en las aportaciones) y no
-- toca el saldo de gastos. Es el reverso de retirada_ahorro.

create table public.deposito_ahorro (
    id         uuid primary key default gen_random_uuid(),
    hogar_id   uuid not null references public.hogar (id) on delete cascade,
    miembro_id uuid not null,
    fecha      date not null,
    importe    numeric(12,2) not null check (importe > 0),
    concepto   text check (length(concepto) <= 200),
    unique (hogar_id, id),
    foreign key (hogar_id, miembro_id) references public.miembro (hogar_id, id)
);

create index deposito_ahorro_fecha_idx on public.deposito_ahorro (hogar_id, fecha);

alter table public.deposito_ahorro enable row level security;

-- Solo lectura para los roles de cliente: toda escritura pasa por Core.Api (ver cerrar_escritura_directa).
create policy deposito_ahorro_select on public.deposito_ahorro
    for select to authenticated
    using (hogar_id in (select public.mis_hogares()));

revoke insert, update, delete, truncate, references, trigger on public.deposito_ahorro from anon, authenticated;
revoke select on public.deposito_ahorro from anon;
