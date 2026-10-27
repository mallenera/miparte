--
-- Name: reembolso_cuenta; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.reembolso_cuenta ENABLE ROW LEVEL SECURITY;

--
-- Name: reembolso_cuenta api_servicio; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY api_servicio ON public.reembolso_cuenta TO miparte_api USING (true) WITH CHECK (true);


--
-- Name: reembolso_cuenta reembolso_cuenta_hogar; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reembolso_cuenta_hogar ON public.reembolso_cuenta TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares))) WITH CHECK ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: TABLE reembolso_cuenta; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.reembolso_cuenta FROM anon, authenticated, miparte_api;

GRANT SELECT ON TABLE public.reembolso_cuenta TO authenticated;
GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE public.reembolso_cuenta TO miparte_api;
