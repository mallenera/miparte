--
-- Name: auditoria_sin_truncate(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.auditoria_sin_truncate() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO ''
    AS $$
begin
    raise exception 'La auditoría es de solo añadir: no se puede vaciar';
end
$$;


--
-- Name: FUNCTION auditoria_sin_truncate(); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.auditoria_sin_truncate() FROM PUBLIC;
