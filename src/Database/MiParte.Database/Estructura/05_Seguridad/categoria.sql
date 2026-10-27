--
-- Name: categoria; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.categoria ENABLE ROW LEVEL SECURITY;

--
-- Name: categoria api_servicio; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY api_servicio ON public.categoria TO miparte_api USING (true) WITH CHECK (true);


--
-- Name: categoria categoria_hogar; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY categoria_hogar ON public.categoria TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares))) WITH CHECK ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: TABLE categoria; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.categoria FROM anon, authenticated, miparte_api;

GRANT SELECT ON TABLE public.categoria TO authenticated;
GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE public.categoria TO miparte_api;
