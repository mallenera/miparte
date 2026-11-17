-- Supabase concede EXECUTE sobre toda función nueva de public a anon y authenticated (privilegios por defecto explícitos a cada
-- rol, que el «revoke ... from public» de cada fichero de Estructura/02_Funciones no quita). Así, una función SECURITY DEFINER
-- quedaba invocable por /rest/v1/rpc saltándose Core.Api: crear_hogar sin el tope de hogares ni el limitador, aceptar_invitacion
-- sin el limitador, exigir_mes_abierto revelando los meses cerrados de cualquier hogar, y mi_miembro_id incluso sin sesión.
-- 00_Esquema.sql ya retira esos privilegios por defecto para las funciones nuevas; aquí se quitan los ya concedidos.
-- authenticated conserva solo las funciones que la estructura le concede porque las usan las políticas RLS o sus disparadores.
-- Revocar es idempotente.
do $$
declare
    f regprocedure;
    nombre text;
begin
    for f, nombre in
        select p.oid::regprocedure, p.proname
          from pg_catalog.pg_proc p
         where p.pronamespace = 'public'::regnamespace
           and p.prokind = 'f'
    loop
        execute pg_catalog.format('revoke execute on function %s from anon', f);
        if nombre not in ('mis_hogares', 'mi_miembro_id', 'es_admin_hogar', 'hogar_tiene_otro_admin') then
            execute pg_catalog.format('revoke execute on function %s from authenticated', f);
        end if;
    end loop;
end
$$;
