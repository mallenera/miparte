--
-- Name: hogar_tiene_otro_admin(uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.hogar_tiene_otro_admin(p_hogar uuid, p_excluir uuid) RETURNS boolean
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO ''
    AS $$
    select exists (
        select 1 from public.miembro m
         where m.hogar_id = p_hogar
           and m.id <> p_excluir
           and m.rol = 'admin'
           and m.activo
           and m.user_id is not null
    );
$$;


--
-- Name: FUNCTION hogar_tiene_otro_admin(p_hogar uuid, p_excluir uuid); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.hogar_tiene_otro_admin(p_hogar uuid, p_excluir uuid) FROM PUBLIC;
GRANT ALL ON FUNCTION public.hogar_tiene_otro_admin(p_hogar uuid, p_excluir uuid) TO authenticated;
