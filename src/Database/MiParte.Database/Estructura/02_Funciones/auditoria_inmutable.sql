--
-- Name: auditoria_inmutable(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.auditoria_inmutable() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO ''
    AS $$
begin
    -- El borrado en cascada desde hogar corre dentro de un trigger de integridad referencial
    -- (profundidad > 1); cualquier otro DELETE o UPDATE, aunque sea del propietario, se rechaza.
    if tg_op = 'DELETE' and pg_trigger_depth() > 1 then
        return old;
    end if;
    raise exception 'La auditoría es de solo añadir: no se puede modificar ni borrar';
end
$$;


--
-- Name: FUNCTION auditoria_inmutable(); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.auditoria_inmutable() FROM PUBLIC;
