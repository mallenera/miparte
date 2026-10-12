--
-- Name: bloquear_mes_hogar(uuid, date); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.bloquear_mes_hogar(p_hogar_id uuid, p_fecha date) RETURNS void
    LANGUAGE sql SECURITY DEFINER
    SET search_path TO ''
    AS $$
    select pg_catalog.pg_advisory_xact_lock(
        pg_catalog.hashtextextended('mes_cerrado:' || p_hogar_id::text || ':' || pg_catalog.date_trunc('month', p_fecha)::date::text, 0));
$$;


--
-- Name: FUNCTION bloquear_mes_hogar(p_hogar_id uuid, p_fecha date); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.bloquear_mes_hogar(p_hogar_id uuid, p_fecha date) FROM PUBLIC;
