-- Perfil «Cuenta común (ahorro)»: lo siembra en los hogares que no tengan ninguno de ese modo (los nuevos ya lo reciben al
-- crearse) y pasa a él los gastos ya pagados desde el ahorro con el perfil de cuenta común, para que al editarlos el formulario
-- muestre el perfil que corresponde. No cambia importes ni repartos (esos gastos no se reparten entre personas).
-- Idempotente: no inserta si el hogar ya tiene un perfil de ese modo o ese nombre, y el update solo toca gastos con perfil cuenta_comun.
insert into public.perfil_reparto (hogar_id, nombre, modo)
select h.id, 'Cuenta común (ahorro)', 'cuenta_comun_ahorro'
  from public.hogar h
 where not exists (select 1 from public.perfil_reparto p where p.hogar_id = h.id and p.modo = 'cuenta_comun_ahorro')
on conflict (hogar_id, nombre) do nothing;

update public.gasto g
   set perfil_reparto_id = a.id
  from public.perfil_reparto c, public.perfil_reparto a
 where g.pagado_desde_ahorro
   and c.hogar_id = g.hogar_id and c.id = g.perfil_reparto_id and c.modo = 'cuenta_comun'
   and a.id = (select min(x.id::text)::uuid from public.perfil_reparto x
                where x.hogar_id = g.hogar_id and x.modo = 'cuenta_comun_ahorro');
