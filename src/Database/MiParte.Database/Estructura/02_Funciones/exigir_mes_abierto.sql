--
-- Name: exigir_mes_abierto(uuid, date[]); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.exigir_mes_abierto(p_hogar_id uuid, p_fechas date[]) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO ''
    AS $$
declare
    v_mes date;
begin
    if not exists (select 1 from public.hogar where id = p_hogar_id) then
        return;
    end if;
    for v_mes in
        select distinct pg_catalog.date_trunc('month', f)::date from pg_catalog.unnest(p_fechas) as f order by 1
    loop
        perform public.bloquear_mes_hogar(p_hogar_id, v_mes);
        -- Consulta después del bloqueo: en READ COMMITTED ve ya el cierre confirmado por la otra transacción.
        if exists (select 1 from public.mes_cerrado where hogar_id = p_hogar_id and mes = v_mes) then
            raise exception 'Ese mes está cerrado: un administrador debe reabrirlo para cambiar sus gastos.'
                using errcode = 'MP409', hint = pg_catalog.to_char(v_mes, 'YYYY-MM');
        end if;
    end loop;
end;
$$;


--
-- Name: FUNCTION exigir_mes_abierto(p_hogar_id uuid, p_fechas date[]); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.exigir_mes_abierto(p_hogar_id uuid, p_fechas date[]) FROM PUBLIC;
