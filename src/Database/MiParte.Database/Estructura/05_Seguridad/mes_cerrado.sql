--
-- Name: mes_cerrado; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.mes_cerrado ENABLE ROW LEVEL SECURITY;

--
-- Name: mes_cerrado api_servicio; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY api_servicio ON public.mes_cerrado TO miparte_api USING (true) WITH CHECK (true);


--
-- Name: mes_cerrado mes_cerrado_select; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY mes_cerrado_select ON public.mes_cerrado FOR SELECT TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: TABLE mes_cerrado; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.mes_cerrado FROM anon, authenticated, miparte_api;

GRANT SELECT ON TABLE public.mes_cerrado TO authenticated;
GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE public.mes_cerrado TO miparte_api;
