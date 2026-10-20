--
-- Name: mi_miembro_id(uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.mi_miembro_id(p_hogar uuid) RETURNS uuid
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO ''
    AS $$
    select m.id
      from public.miembro m
     where m.hogar_id = p_hogar
       and m.user_id = auth.uid()
       and m.activo
     limit 1;
$$;


--
-- Name: FUNCTION mi_miembro_id(p_hogar uuid); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.mi_miembro_id(p_hogar uuid) FROM PUBLIC;
GRANT ALL ON FUNCTION public.mi_miembro_id(p_hogar uuid) TO authenticated;
