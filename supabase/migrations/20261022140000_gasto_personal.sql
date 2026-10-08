-- Gastos personales: gastos de un solo miembro que quedan fuera de la liquidación.
--
-- Un gasto es_personal lo paga y lo asume un único adulto: su reparto es el 100 % del pagador (como el perfil
-- 'individual'), no genera deuda entre personas, no entra en la liquidación ni en los totales del hogar, y se ve en
-- los listados y en el resumen del miembro. No puede ir a cargo de la cuenta común ni pagarse con el ahorro.

alter table public.gasto add column es_personal boolean not null default false;

alter table public.gasto
    add constraint gasto_es_personal_check
    check (not es_personal or (pagado_por is not null and not a_cargo_cuenta_comun and not pagado_desde_ahorro));
