--
-- Name: aportacion_cuenta; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.aportacion_cuenta ENABLE ROW LEVEL SECURITY;

--
-- Name: aportacion_cuenta api_servicio; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY api_servicio ON public.aportacion_cuenta TO miparte_api USING (true) WITH CHECK (true);


--
-- Name: aportacion_cuenta aportacion_cuenta_hogar; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY aportacion_cuenta_hogar ON public.aportacion_cuenta TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares))) WITH CHECK ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: TABLE aportacion_cuenta; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.aportacion_cuenta FROM anon, authenticated, miparte_api;

GRANT SELECT ON TABLE public.aportacion_cuenta TO authenticated;
GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE public.aportacion_cuenta TO miparte_api;
