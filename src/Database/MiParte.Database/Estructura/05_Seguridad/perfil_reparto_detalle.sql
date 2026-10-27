--
-- Name: perfil_reparto_detalle; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.perfil_reparto_detalle ENABLE ROW LEVEL SECURITY;

--
-- Name: perfil_reparto_detalle api_servicio; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY api_servicio ON public.perfil_reparto_detalle TO miparte_api USING (true) WITH CHECK (true);


--
-- Name: perfil_reparto_detalle perfil_reparto_detalle_hogar; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY perfil_reparto_detalle_hogar ON public.perfil_reparto_detalle TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares))) WITH CHECK ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: TABLE perfil_reparto_detalle; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.perfil_reparto_detalle FROM anon, authenticated, miparte_api;

GRANT SELECT ON TABLE public.perfil_reparto_detalle TO authenticated;
GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE public.perfil_reparto_detalle TO miparte_api;
