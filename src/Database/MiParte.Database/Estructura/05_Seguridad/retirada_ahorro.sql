--
-- Name: retirada_ahorro; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.retirada_ahorro ENABLE ROW LEVEL SECURITY;

--
-- Name: retirada_ahorro retirada_ahorro_select; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY retirada_ahorro_select ON public.retirada_ahorro FOR SELECT TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: TABLE retirada_ahorro; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.retirada_ahorro FROM anon, authenticated;

GRANT SELECT ON TABLE public.retirada_ahorro TO authenticated;
