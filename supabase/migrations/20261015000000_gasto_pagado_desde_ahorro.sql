-- Gasto pagado desde el ahorro de la cuenta común.
--
-- Un gasto con pagado_desde_ahorro descuenta su importe del ahorro disponible (no del saldo de gastos), no lo
-- adelanta nadie (sin pagado_por, sin reembolso pendiente) y no se reparte entre personas: va a cargo de la cuenta
-- común. Core.Api impide pagar con ahorro más de lo ahorrado.

alter table public.gasto add column pagado_desde_ahorro boolean not null default false;

alter table public.gasto
    add constraint gasto_pagado_desde_ahorro_check
    check (not pagado_desde_ahorro or (pagado_por is null and a_cargo_cuenta_comun));
