--
-- Name: es_admin_hogar(uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.es_admin_hogar(p_hogar uuid) RETURNS boolean
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO ''
    AS $$
    select exists (
        select 1 from public.miembro m
         where m.hogar_id = p_hogar
           and m.user_id = auth.uid()
           and m.rol = 'admin'
           and m.activo
    );
$$;


--
-- Name: FUNCTION es_admin_hogar(p_hogar uuid); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.es_admin_hogar(p_hogar uuid) FROM PUBLIC;
GRANT ALL ON FUNCTION public.es_admin_hogar(p_hogar uuid) TO authenticated;
