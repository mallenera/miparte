-- Ahorro por hogar.
--
-- hogar.ahorro_activo: el hogar decide si usa el ahorro de la cuenta común (parte de ahorro de las aportaciones, depósitos,
-- retiradas y gastos pagados desde el ahorro). Core.Api rechaza esas escrituras mientras esté desactivado; lo activa un admin
-- y solo tiene efecto con la cuenta común activada.

alter table public.hogar add column ahorro_activo boolean not null default false;

-- Los hogares que ya usaban el ahorro no pierden nada al desplegar: se activa donde hay datos.
update public.hogar h
   set ahorro_activo = true
 where exists (select 1 from public.aportacion_cuenta a where a.hogar_id = h.id and a.ahorro > 0)
    or exists (select 1 from public.deposito_ahorro d where d.hogar_id = h.id)
    or exists (select 1 from public.retirada_ahorro r where r.hogar_id = h.id)
    or exists (select 1 from public.gasto g where g.hogar_id = h.id and g.pagado_desde_ahorro);
