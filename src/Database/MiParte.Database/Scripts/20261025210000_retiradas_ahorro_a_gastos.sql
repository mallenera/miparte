-- Las retiradas de ahorro pasan a ser gastos con el perfil «Cuenta común (ahorro)», pagados por el propio ahorro: así se ven en
-- la pestaña Gastos y en los movimientos como «Gasto desde el ahorro», y la pantalla de la cuenta común deja de mostrar retiradas.
-- Los saldos no cambian: una retirada y un gasto pagado desde el ahorro restan igual del ahorro disponible y del banco.
--
-- Cada gasto conserva el id, la fecha, el importe y el concepto de su retirada (sin concepto: «Retirada de ahorro»), y va a una
-- categoría raíz «Retiradas de ahorro» a cargo de la cuenta común (ahorro), que se crea en los hogares que la necesiten. Quién la
-- registró no se guarda en el gasto (sigue en el historial de auditoría).
--
-- Las retiradas de un mes cerrado se quedan como están: el disparador gasto_mes_abierto no deja anotar gastos en él. Se convertirán
-- si se vuelve a ejecutar este paso tras reabrir el mes (no se reejecuta solo: el journal lo da por aplicado).
--
-- Idempotente: solo toca retiradas de hogares con perfil de cuenta común (ahorro); la categoría se inserta si falta (on conflict) y
-- el gasto reutiliza el id de la retirada (on conflict do nothing); después se borran las retiradas que ya tienen su gasto.
with convertibles as (
    select r.hogar_id
      from public.retirada_ahorro r
     where exists (select 1 from public.perfil_reparto p where p.hogar_id = r.hogar_id and p.modo = 'cuenta_comun_ahorro')
       and not exists (select 1 from public.mes_cerrado m
                        where m.hogar_id = r.hogar_id and m.mes = date_trunc('month', r.fecha)::date)
     group by r.hogar_id
)
insert into public.categoria (hogar_id, nombre, perfil_reparto_id, a_cargo_cuenta_comun)
select c.hogar_id, 'Retiradas de ahorro',
       (select min(p.id::text)::uuid from public.perfil_reparto p where p.hogar_id = c.hogar_id and p.modo = 'cuenta_comun_ahorro'),
       true
  from convertibles c
on conflict do nothing;

insert into public.gasto (id, hogar_id, fecha, importe, categoria_id, pagado_por, perfil_reparto_id, concepto,
                          a_cargo_cuenta_comun, pagado_desde_ahorro)
select r.id, r.hogar_id, r.fecha, r.importe, cat.id, null, per.id, coalesce(r.concepto, 'Retirada de ahorro'), true, true
  from public.retirada_ahorro r
  join lateral (select min(p.id::text)::uuid as id from public.perfil_reparto p
                 where p.hogar_id = r.hogar_id and p.modo = 'cuenta_comun_ahorro') per on per.id is not null
  join public.categoria cat
    on cat.hogar_id = r.hogar_id and cat.categoria_padre_id is null and cat.nombre = 'Retiradas de ahorro'
 where not exists (select 1 from public.mes_cerrado m
                    where m.hogar_id = r.hogar_id and m.mes = date_trunc('month', r.fecha)::date)
on conflict (id) do nothing;

delete from public.retirada_ahorro r
 using public.gasto g
 where g.id = r.id and g.hogar_id = r.hogar_id and g.pagado_desde_ahorro;
