-- Cuenta común por hogar y «a cargo de la cuenta» por categoría.
--
-- hogar.cuenta_comun_activa: el hogar decide si usa la cuenta común (aportaciones, saldo, reembolsos, ahorro). Core.Api rechaza
-- las escrituras de la cuenta y los gastos a su cargo mientras esté desactivada; la activa un admin.
-- categoria.a_cargo_cuenta_comun: los gastos de la categoría van por defecto a cargo de la cuenta común. Core.Api exige que
-- su perfil por defecto sea el de cuenta común; aquí solo se garantiza que la columna no es nula.

alter table public.hogar add column cuenta_comun_activa boolean not null default false;
alter table public.categoria add column a_cargo_cuenta_comun boolean not null default false;

-- Los hogares que ya usaban la cuenta común no pierden nada al desplegar: se activa donde hay datos.
update public.hogar h
   set cuenta_comun_activa = true
 where exists (select 1 from public.aportacion_cuenta a where a.hogar_id = h.id)
    or exists (select 1 from public.deposito_ahorro d where d.hogar_id = h.id)
    or exists (select 1 from public.retirada_ahorro r where r.hogar_id = h.id)
    or exists (select 1 from public.reembolso_cuenta r where r.hogar_id = h.id)
    or exists (select 1 from public.gasto g where g.hogar_id = h.id and g.a_cargo_cuenta_comun);

-- Las categorías cuyo perfil por defecto ya era el de cuenta común quedan marcadas.
update public.categoria c
   set a_cargo_cuenta_comun = true
  from public.perfil_reparto p
 where p.hogar_id = c.hogar_id and p.id = c.perfil_reparto_id and p.modo = 'cuenta_comun';
