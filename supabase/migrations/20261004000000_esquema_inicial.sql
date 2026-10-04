-- Mi Parte, Tu Parte: esquema inicial (9 tablas base) con Row Level Security.
--
-- Convenciones:
--  * Todas las tablas de negocio llevan hogar_id para aislar los datos de cada hogar.
--  * Las claves foráneas entre tablas son compuestas (hogar_id, id): así un registro
--    de un hogar no puede referenciar datos de otro hogar.
--  * Importes en numeric(12,2); porcentajes/partes en numeric(12,4).
--  * Validaciones que abarcan varias filas (porcentajes que suman 100, que
--    gasto_reparto sume el importe del gasto) se hacen en Core.Domain/Core.Api.
--
-- Nota sobre RLS: protege el acceso vía Supabase (PostgREST / clientes con JWT).
-- Si Core.Api conecta con un rol que se salta RLS (p. ej. postgres), debe filtrar
-- siempre por hogar_id en código (EF Core global query filter).

-- ---------------------------------------------------------------------------
-- Tablas
-- ---------------------------------------------------------------------------

create table public.hogar (
    id        uuid primary key default gen_random_uuid(),
    nombre    text not null check (length(btrim(nombre)) > 0),
    creado_en timestamptz not null default now()
);

create table public.miembro (
    id             uuid primary key default gen_random_uuid(),
    hogar_id       uuid not null references public.hogar (id) on delete cascade,
    nombre         text not null check (length(btrim(nombre)) > 0),
    tipo           text not null check (tipo in ('adulto', 'a_cargo')),
    responsable_id uuid,
    user_id        uuid references auth.users (id) on delete set null,
    activo         boolean not null default true,
    unique (hogar_id, id),
    unique (hogar_id, user_id),
    foreign key (hogar_id, responsable_id) references public.miembro (hogar_id, id),
    -- una persona a cargo necesita responsable; un adulto no lo tiene
    check ((tipo = 'a_cargo') = (responsable_id is not null))
);

create table public.perfil_reparto (
    id       uuid primary key default gen_random_uuid(),
    hogar_id uuid not null references public.hogar (id) on delete cascade,
    nombre   text not null check (length(btrim(nombre)) > 0),
    modo     text not null check (modo in ('porcentaje', 'partes', 'ingresos', 'individual')),
    unique (hogar_id, id),
    unique (hogar_id, nombre)
);

-- No se usa en modo 'ingresos' (se calcula con los ingresos reales del mes).
create table public.perfil_reparto_detalle (
    id         uuid primary key default gen_random_uuid(),
    hogar_id   uuid not null references public.hogar (id) on delete cascade,
    perfil_id  uuid not null,
    miembro_id uuid not null,
    valor      numeric(12,4) not null check (valor >= 0),
    unique (perfil_id, miembro_id),
    foreign key (hogar_id, perfil_id)  references public.perfil_reparto (hogar_id, id) on delete cascade,
    foreign key (hogar_id, miembro_id) references public.miembro (hogar_id, id)
);

create table public.categoria (
    id                 uuid primary key default gen_random_uuid(),
    hogar_id           uuid not null references public.hogar (id) on delete cascade,
    nombre             text not null check (length(btrim(nombre)) > 0),
    categoria_padre_id uuid,
    perfil_reparto_id  uuid,
    unique (hogar_id, id),
    unique nulls not distinct (hogar_id, categoria_padre_id, nombre),
    foreign key (hogar_id, categoria_padre_id) references public.categoria (hogar_id, id),
    foreign key (hogar_id, perfil_reparto_id)  references public.perfil_reparto (hogar_id, id)
);

create table public.ingreso (
    id         uuid primary key default gen_random_uuid(),
    hogar_id   uuid not null references public.hogar (id) on delete cascade,
    miembro_id uuid not null,
    fecha      date not null,
    importe    numeric(12,2) not null check (importe > 0),
    concepto   text,
    foreign key (hogar_id, miembro_id) references public.miembro (hogar_id, id)
);

create table public.gasto_recurrente (
    id                uuid primary key default gen_random_uuid(),
    hogar_id          uuid not null references public.hogar (id) on delete cascade,
    importe           numeric(12,2) not null check (importe > 0),
    categoria_id      uuid not null,
    pagado_por        uuid not null,
    perfil_reparto_id uuid not null,
    dia_mes           smallint not null check (dia_mes between 1 and 28),
    concepto          text,
    activo            boolean not null default true,
    unique (hogar_id, id),
    foreign key (hogar_id, categoria_id)      references public.categoria (hogar_id, id),
    foreign key (hogar_id, pagado_por)        references public.miembro (hogar_id, id),
    foreign key (hogar_id, perfil_reparto_id) references public.perfil_reparto (hogar_id, id)
);

create table public.gasto (
    id                   uuid primary key default gen_random_uuid(),
    hogar_id             uuid not null references public.hogar (id) on delete cascade,
    fecha                date not null,
    importe              numeric(12,2) not null check (importe > 0),
    categoria_id         uuid not null,
    pagado_por           uuid not null,
    perfil_reparto_id    uuid not null,
    concepto             text,
    gasto_recurrente_id  uuid,
    unique (hogar_id, id),
    foreign key (hogar_id, categoria_id)         references public.categoria (hogar_id, id),
    foreign key (hogar_id, pagado_por)           references public.miembro (hogar_id, id),
    foreign key (hogar_id, perfil_reparto_id)    references public.perfil_reparto (hogar_id, id),
    foreign key (hogar_id, gasto_recurrente_id)  references public.gasto_recurrente (hogar_id, id)
);

-- Resultado del reparto calculado al registrar el gasto: el histórico no cambia
-- si luego se modifican sueldos o perfiles.
create table public.gasto_reparto (
    gasto_id        uuid not null,
    miembro_id      uuid not null,
    hogar_id        uuid not null references public.hogar (id) on delete cascade,
    importe_asumido numeric(12,2) not null check (importe_asumido >= 0),
    primary key (gasto_id, miembro_id),
    foreign key (hogar_id, gasto_id)   references public.gasto (hogar_id, id) on delete cascade,
    foreign key (hogar_id, miembro_id) references public.miembro (hogar_id, id)
);

-- ---------------------------------------------------------------------------
-- Índices (las claves compuestas y unique ya cubren varios accesos)
-- ---------------------------------------------------------------------------

create index miembro_user_id_idx    on public.miembro (user_id);
create index ingreso_mes_idx        on public.ingreso (hogar_id, fecha);
create index gasto_mes_idx          on public.gasto (hogar_id, fecha);
create index gasto_categoria_idx    on public.gasto (hogar_id, categoria_id);
create index gasto_reparto_miembro  on public.gasto_reparto (hogar_id, miembro_id);

-- ---------------------------------------------------------------------------
-- Row Level Security
-- ---------------------------------------------------------------------------

-- Hogares a los que pertenece el usuario autenticado. SECURITY DEFINER para
-- evitar recursión de políticas al consultar miembro desde su propia política.
create function public.mis_hogares()
returns setof uuid
language sql
stable
security definer
set search_path = ''
as $$
    select m.hogar_id from public.miembro m where m.user_id = auth.uid();
$$;

revoke all on function public.mis_hogares() from public, anon;
grant execute on function public.mis_hogares() to authenticated;

alter table public.hogar enable row level security;

create policy hogar_select on public.hogar
    for select to authenticated
    using (id in (select public.mis_hogares()));

create policy hogar_update on public.hogar
    for update to authenticated
    using (id in (select public.mis_hogares()))
    with check (id in (select public.mis_hogares()));

-- Las altas de hogar pasan por public.crear_hogar (no hay policy de insert).

do $$
declare
    t text;
begin
    foreach t in array array[
        'miembro', 'perfil_reparto', 'perfil_reparto_detalle', 'categoria',
        'ingreso', 'gasto_recurrente', 'gasto', 'gasto_reparto'
    ]
    loop
        execute format('alter table public.%I enable row level security', t);
        execute format(
            'create policy %I on public.%I for all to authenticated
                 using (hogar_id in (select public.mis_hogares()))
                 with check (hogar_id in (select public.mis_hogares()))',
            t || '_hogar', t);
    end loop;
end
$$;

-- Crea un hogar y da de alta al usuario autenticado como su primer miembro adulto.
create function public.crear_hogar(p_nombre_hogar text, p_nombre_miembro text)
returns uuid
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_hogar uuid;
begin
    if auth.uid() is null then
        raise exception 'Usuario no autenticado';
    end if;

    insert into public.hogar (nombre) values (p_nombre_hogar) returning id into v_hogar;
    insert into public.miembro (hogar_id, nombre, tipo, user_id)
    values (v_hogar, p_nombre_miembro, 'adulto', auth.uid());

    return v_hogar;
end
$$;

revoke all on function public.crear_hogar(text, text) from public, anon;
grant execute on function public.crear_hogar(text, text) to authenticated;
