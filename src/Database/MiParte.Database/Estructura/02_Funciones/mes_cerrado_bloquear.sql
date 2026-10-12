--
-- Name: mes_cerrado_bloquear(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.mes_cerrado_bloquear() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO ''
    AS $$
begin
    perform public.bloquear_mes_hogar(new.hogar_id, new.mes);
    return new;
end;
$$;


--
-- Name: FUNCTION mes_cerrado_bloquear(); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.mes_cerrado_bloquear() FROM PUBLIC;
