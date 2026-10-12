--
-- Name: miembro; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.miembro ENABLE ROW LEVEL SECURITY;

--
-- Name: miembro miembro_delete; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY miembro_delete ON public.miembro FOR DELETE TO authenticated USING (public.es_admin_hogar(hogar_id));


--
-- Name: miembro miembro_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY miembro_insert ON public.miembro FOR INSERT TO authenticated WITH CHECK (public.es_admin_hogar(hogar_id));


--
-- Name: miembro miembro_select; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY miembro_select ON public.miembro FOR SELECT TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: miembro miembro_update; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY miembro_update ON public.miembro FOR UPDATE TO authenticated USING ((public.es_admin_hogar(hogar_id) OR ((user_id = auth.uid()) AND (hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares))))) WITH CHECK ((public.es_admin_hogar(hogar_id) OR ((user_id = auth.uid()) AND (hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)))));


--
-- Name: TABLE miembro; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.miembro FROM anon, authenticated;

GRANT MAINTAIN ON TABLE public.miembro TO anon;
GRANT SELECT,MAINTAIN ON TABLE public.miembro TO authenticated;
