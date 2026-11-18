--
-- Name: enlace_grupo; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.enlace_grupo ENABLE ROW LEVEL SECURITY;

--
-- Name: enlace_grupo api_servicio; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY api_servicio ON public.enlace_grupo TO miparte_api USING (true) WITH CHECK (true);


--
-- Name: enlace_grupo enlace_grupo_delete; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY enlace_grupo_delete ON public.enlace_grupo FOR DELETE TO authenticated USING (public.es_admin_hogar(hogar_id));


--
-- Name: enlace_grupo enlace_grupo_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY enlace_grupo_insert ON public.enlace_grupo FOR INSERT TO authenticated WITH CHECK (public.es_admin_hogar(hogar_id));


--
-- Name: enlace_grupo enlace_grupo_select; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY enlace_grupo_select ON public.enlace_grupo FOR SELECT TO authenticated USING (public.es_admin_hogar(hogar_id));


--
-- Name: enlace_grupo enlace_grupo_update; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY enlace_grupo_update ON public.enlace_grupo FOR UPDATE TO authenticated USING (public.es_admin_hogar(hogar_id)) WITH CHECK (public.es_admin_hogar(hogar_id));


--
-- Name: TABLE enlace_grupo; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.enlace_grupo FROM anon, authenticated, miparte_api;

GRANT SELECT ON TABLE public.enlace_grupo TO authenticated;
GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE public.enlace_grupo TO miparte_api;
