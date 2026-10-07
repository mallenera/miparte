-- Perfiles de reparto acordados: individual, por porcentajes, por partes (personas) y cuenta común.
-- Los ingresos del hogar no se guardan: se elimina la tabla ingreso y el modo 'ingresos' de los perfiles.
-- Un gasto con perfil 'cuenta_comun' lo asume la cuenta común: no se reparte entre personas ni genera deuda.

drop table public.ingreso;

-- Los perfiles 'ingresos' existentes pasan a 'partes' con 1 parte por adulto activo (reparto a partes iguales).
insert into public.perfil_reparto_detalle (hogar_id, perfil_id, miembro_id, valor)
select p.hogar_id, p.id, m.id, 1
  from public.perfil_reparto p
  join public.miembro m on m.hogar_id = p.hogar_id and m.tipo = 'adulto' and m.activo
 where p.modo = 'ingresos';

update public.perfil_reparto set modo = 'partes' where modo = 'ingresos';

update public.perfil_reparto set nombre = 'Partes iguales'
 where nombre = 'Proporcional a ingresos'
   and not exists (select 1 from public.perfil_reparto o
                    where o.hogar_id = perfil_reparto.hogar_id and o.nombre = 'Partes iguales');

alter table public.perfil_reparto drop constraint perfil_reparto_modo_check;
alter table public.perfil_reparto
    add constraint perfil_reparto_modo_check
    check (modo in ('porcentaje', 'partes', 'cuenta_comun', 'individual'));

-- Gasto a cargo de la cuenta común: sin filas en gasto_reparto y fuera de la liquidación entre personas.
alter table public.gasto add column a_cargo_cuenta_comun boolean not null default false;

-- crear_hogar: perfiles Por partes, Porcentaje fijo, Individual y Cuenta común. Para que un hogar de un
-- solo adulto funcione desde el principio, el creador recibe 1 parte y el 100 %.
create or replace function public.crear_hogar(p_nombre_hogar text, p_nombre_miembro text)
returns uuid
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_hogar    uuid;
    v_miembro  uuid;
    v_partes   uuid;
    v_porcent  uuid;
    v_indiv    uuid;
    v_cuenta   uuid;
begin
    if auth.uid() is null then
        raise exception 'Usuario no autenticado';
    end if;

    insert into public.hogar (nombre) values (p_nombre_hogar) returning id into v_hogar;
    insert into public.miembro (hogar_id, nombre, tipo, user_id, rol)
    values (v_hogar, p_nombre_miembro, 'adulto', auth.uid(), 'admin')
    returning id into v_miembro;

    insert into public.perfil_reparto (hogar_id, nombre, modo)
    values (v_hogar, 'Cuenta común', 'cuenta_comun') returning id into v_cuenta;
    insert into public.perfil_reparto (hogar_id, nombre, modo)
    values (v_hogar, 'Por partes', 'partes') returning id into v_partes;
    insert into public.perfil_reparto (hogar_id, nombre, modo)
    values (v_hogar, 'Porcentaje fijo', 'porcentaje') returning id into v_porcent;
    insert into public.perfil_reparto (hogar_id, nombre, modo)
    values (v_hogar, 'Individual', 'individual') returning id into v_indiv;

    insert into public.perfil_reparto_detalle (hogar_id, perfil_id, miembro_id, valor) values
        (v_hogar, v_partes,  v_miembro, 1),
        (v_hogar, v_porcent, v_miembro, 100);

    insert into public.categoria (hogar_id, nombre, perfil_reparto_id) values
        (v_hogar, 'Hipoteca/Alquiler',       v_partes),
        (v_hogar, 'Alimentación',            v_partes),
        (v_hogar, 'Suministros',             v_partes),
        (v_hogar, 'Gastos varios de casa',   v_partes),
        (v_hogar, 'Hijo',                    v_partes),
        (v_hogar, 'Ocio personal',           v_indiv);

    return v_hogar;
end
$$;

revoke all on function public.crear_hogar(text, text) from public, anon;
grant execute on function public.crear_hogar(text, text) to authenticated;
