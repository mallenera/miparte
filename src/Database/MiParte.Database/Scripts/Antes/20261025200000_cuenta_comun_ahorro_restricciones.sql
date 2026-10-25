-- Perfil «Cuenta común (ahorro)»: amplía dos restricciones con el mismo nombre, que la estructura no reemplaza por sí sola.
-- Se borran aquí y la estructura las vuelve a crear con la definición nueva:
--   perfil_reparto_modo_check admite el modo 'cuenta_comun_ahorro';
--   gasto_pagado_desde_ahorro_check deja que un adulto adelante un gasto pagado desde el ahorro (antes exigía pagado_por nulo).
-- Antes de la estructura. Idempotente.
alter table if exists public.perfil_reparto drop constraint if exists perfil_reparto_modo_check;
alter table if exists public.gasto drop constraint if exists gasto_pagado_desde_ahorro_check;
