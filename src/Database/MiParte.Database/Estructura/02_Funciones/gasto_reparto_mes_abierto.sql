--
-- Name: gasto_reparto_mes_abierto(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.gasto_reparto_mes_abierto() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO ''
    AS $$
declare
    v_hogar uuid := case when tg_op = 'INSERT' then new.hogar_id else old.hogar_id end;
    v_gasto uuid := case when tg_op = 'INSERT' then new.gasto_id else old.gasto_id end;
    v_fecha date;
begin
    select fecha into v_fecha from public.gasto where hogar_id = v_hogar and id = v_gasto;
    -- Sin gasto (borrado en cascada de un gasto ya eliminado): el trigger de gasto ya lo comprobó.
    if v_fecha is not null then
        perform public.exigir_mes_abierto(v_hogar, array[v_fecha]);
    end if;
    return case when tg_op = 'DELETE' then old else new end;
end;
$$;


--
-- Name: FUNCTION gasto_reparto_mes_abierto(); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.gasto_reparto_mes_abierto() FROM PUBLIC;
