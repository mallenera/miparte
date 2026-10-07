-- Gasto pagado directamente por la cuenta común: pagado_por nulo.
-- La cuenta solo paga gastos a su cargo (perfil 'cuenta_comun'); no genera reembolso pendiente a nadie.

alter table public.gasto alter column pagado_por drop not null;

alter table public.gasto
    add constraint gasto_pagado_por_cuenta_comun_check
    check (pagado_por is not null or a_cargo_cuenta_comun);
