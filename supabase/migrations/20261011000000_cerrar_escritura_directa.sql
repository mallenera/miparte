-- Cierra la escritura directa desde PostgREST.
--
-- La anon key de Supabase es pública (va en el appsettings del front) y las políticas RLS dejaban a
-- cualquier miembro autenticado escribir en las tablas con su JWT, saltándose Core.Api y, con ello, todas sus
-- validaciones (importes, pagador adulto activo, reparto que suma el gasto, tope de hogares, longitudes...).
-- Core.Api se conecta con un rol propietario (no authenticated/anon), así que no necesita estos permisos.
--
-- Decisión: toda escritura pasa por Core.Api. Los roles de cliente conservan SELECT (acotado por RLS al
-- hogar) y pierden INSERT/UPDATE/DELETE/TRUNCATE. Las políticas de escritura se quedan como segunda barrera.

do $$
declare
    t text;
begin
    foreach t in array array[
        'hogar', 'miembro', 'perfil_reparto', 'perfil_reparto_detalle', 'categoria',
        'gasto_recurrente', 'gasto', 'gasto_reparto', 'invitacion_hogar', 'pago_liquidacion',
        'aportacion_cuenta', 'reembolso_cuenta'
    ]
    loop
        execute format('revoke insert, update, delete, truncate, references, trigger on public.%I from anon, authenticated', t);
        -- anon nunca debe leer: todas las políticas son "to authenticated".
        execute format('revoke select on public.%I from anon', t);
    end loop;
end
$$;

-- Las tablas futuras no heredan escritura para los roles de cliente (solo afecta a las creadas por el
-- rol que aplica la migración).
alter default privileges in schema public revoke insert, update, delete, truncate on tables from anon, authenticated;

-- Core.Api replica en EF crear_hogar y aceptar_invitacion (con sus topes y longitudes). Expuestas como RPC
-- permitían crear hogares sin límite y sin validar la longitud de los nombres. mis_hogares y es_admin_hogar
-- siguen ejecutables: las usan las políticas RLS de lectura.
revoke execute on function public.crear_hogar(text, text) from authenticated;
revoke execute on function public.aceptar_invitacion(text, text) from authenticated;

-- Límites de longitud también en la base de datos, por si algún camino de escritura no pasa por la API
-- (los mismos que aplica Core.Api: 100 para nombres y 200 para conceptos). Van NOT VALID: se aplican ya a toda
-- fila nueva o modificada, pero no escanean (ni bloquean) las tablas ni fallan si hubiera una fila antigua que
-- los supere. Cuando se hayan revisado esas filas (si las hay), se validan aparte con
-- alter table public.<tabla> validate constraint <restricción>; (no bloquea las escrituras).
alter table public.hogar            add constraint hogar_nombre_longitud            check (length(nombre) <= 100) not valid;
alter table public.miembro          add constraint miembro_nombre_longitud          check (length(nombre) <= 100) not valid;
alter table public.perfil_reparto   add constraint perfil_reparto_nombre_longitud   check (length(nombre) <= 100) not valid;
alter table public.categoria        add constraint categoria_nombre_longitud        check (length(nombre) <= 100) not valid;
alter table public.gasto            add constraint gasto_concepto_longitud          check (length(concepto) <= 200) not valid;
alter table public.gasto_recurrente add constraint gasto_recurrente_concepto_longitud check (length(concepto) <= 200) not valid;
alter table public.pago_liquidacion add constraint pago_liquidacion_concepto_longitud check (length(concepto) <= 200) not valid;
alter table public.reembolso_cuenta add constraint reembolso_cuenta_concepto_longitud check (length(concepto) <= 200) not valid;
