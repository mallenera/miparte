--
-- Name: perfil_reparto; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.perfil_reparto ENABLE ROW LEVEL SECURITY;

--
-- Name: perfil_reparto api_servicio; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY api_servicio ON public.perfil_reparto TO miparte_api USING (true) WITH CHECK (true);


--
-- Name: perfil_reparto perfil_reparto_hogar; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY perfil_reparto_hogar ON public.perfil_reparto TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares))) WITH CHECK ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: TABLE perfil_reparto; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.perfil_reparto FROM anon, authenticated, miparte_api;

GRANT SELECT ON TABLE public.perfil_reparto TO authenticated;
GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE public.perfil_reparto TO miparte_api;
