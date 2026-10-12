--
-- Name: gasto_mes_abierto(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.gasto_mes_abierto() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO ''
    AS $$
begin
    if tg_op = 'DELETE' then
        perform public.exigir_mes_abierto(old.hogar_id, array[old.fecha]);
        return old;
    elsif tg_op = 'INSERT' then
        perform public.exigir_mes_abierto(new.hogar_id, array[new.fecha]);
    else
        perform public.exigir_mes_abierto(new.hogar_id, array[old.fecha, new.fecha]);
    end if;
    return new;
end;
$$;


--
-- Name: FUNCTION gasto_mes_abierto(); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.gasto_mes_abierto() FROM PUBLIC;
