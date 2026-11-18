--
-- Name: ingreso_cuenta_comun; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.ingreso_cuenta_comun ENABLE ROW LEVEL SECURITY;

--
-- Name: ingreso_cuenta_comun api_servicio; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY api_servicio ON public.ingreso_cuenta_comun TO miparte_api USING (true) WITH CHECK (true);


--
-- Name: ingreso_cuenta_comun ingreso_cuenta_comun_select; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY ingreso_cuenta_comun_select ON public.ingreso_cuenta_comun FOR SELECT TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: TABLE ingreso_cuenta_comun; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.ingreso_cuenta_comun FROM anon, authenticated, miparte_api;

GRANT SELECT ON TABLE public.ingreso_cuenta_comun TO authenticated;
GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE public.ingreso_cuenta_comun TO miparte_api;
