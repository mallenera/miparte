--
-- Name: hogar; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.hogar ENABLE ROW LEVEL SECURITY;

--
-- Name: hogar api_servicio; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY api_servicio ON public.hogar TO miparte_api USING (true) WITH CHECK (true);


--
-- Name: hogar hogar_select; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY hogar_select ON public.hogar FOR SELECT TO authenticated USING ((id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: hogar hogar_update; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY hogar_update ON public.hogar FOR UPDATE TO authenticated USING ((id IN ( SELECT public.mis_hogares() AS mis_hogares))) WITH CHECK ((id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: TABLE hogar; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.hogar FROM anon, authenticated, miparte_api;

GRANT SELECT ON TABLE public.hogar TO authenticated;
GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE public.hogar TO miparte_api;
