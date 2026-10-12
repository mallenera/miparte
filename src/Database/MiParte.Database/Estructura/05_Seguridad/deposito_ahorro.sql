--
-- Name: deposito_ahorro; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.deposito_ahorro ENABLE ROW LEVEL SECURITY;

--
-- Name: deposito_ahorro deposito_ahorro_select; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY deposito_ahorro_select ON public.deposito_ahorro FOR SELECT TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: TABLE deposito_ahorro; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.deposito_ahorro FROM anon, authenticated;

GRANT SELECT ON TABLE public.deposito_ahorro TO authenticated;
