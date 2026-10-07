-- Cuenta común: aportaciones fijas mensuales por adulto y reembolsos a quien adelantó gastos de la cuenta.
--
-- aportacion_cuenta guarda el importe fijo que un adulto aporta desde un mes en adelante: el importe de un
-- mes es el de la fila con 'desde' más reciente que no pase de ese mes. Un importe 0 deja de aportar.
-- reembolso_cuenta es un pago de la cuenta común a la persona que adelantó un gasto cargado a la cuenta
-- (gasto.a_cargo_cuenta_comun): baja lo que la cuenta le debe y el efectivo, pero no el saldo.

create table public.aportacion_cuenta (
    id         uuid primary key default gen_random_uuid(),
    hogar_id   uuid not null references public.hogar (id) on delete cascade,
    miembro_id uuid not null,
    desde      date not null check (extract(day from desde) = 1),
    importe    numeric(12,2) not null check (importe >= 0),
    unique (hogar_id, id),
    unique (hogar_id, miembro_id, desde),
    foreign key (hogar_id, miembro_id) references public.miembro (hogar_id, id)
);

create table public.reembolso_cuenta (
    id         uuid primary key default gen_random_uuid(),
    hogar_id   uuid not null references public.hogar (id) on delete cascade,
    miembro_id uuid not null,
    fecha      date not null,
    importe    numeric(12,2) not null check (importe > 0),
    concepto   text,
    unique (hogar_id, id),
    foreign key (hogar_id, miembro_id) references public.miembro (hogar_id, id)
);

create index reembolso_cuenta_fecha_idx on public.reembolso_cuenta (hogar_id, fecha);

alter table public.aportacion_cuenta enable row level security;
alter table public.reembolso_cuenta enable row level security;

create policy aportacion_cuenta_hogar on public.aportacion_cuenta
    for all to authenticated
    using (hogar_id in (select public.mis_hogares()))
    with check (hogar_id in (select public.mis_hogares()));

create policy reembolso_cuenta_hogar on public.reembolso_cuenta
    for all to authenticated
    using (hogar_id in (select public.mis_hogares()))
    with check (hogar_id in (select public.mis_hogares()));
