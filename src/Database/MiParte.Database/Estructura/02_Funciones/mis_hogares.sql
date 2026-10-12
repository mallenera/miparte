--
-- Name: mis_hogares(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.mis_hogares() RETURNS SETOF uuid
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO ''
    AS $$
    select m.hogar_id
      from public.miembro m
     where m.user_id = auth.uid()
       and m.activo = true;
$$;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: FUNCTION mis_hogares(); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.mis_hogares() FROM PUBLIC;
GRANT ALL ON FUNCTION public.mis_hogares() TO authenticated;
