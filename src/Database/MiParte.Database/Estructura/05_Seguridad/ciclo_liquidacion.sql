--
-- Name: ciclo_liquidacion; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.ciclo_liquidacion ENABLE ROW LEVEL SECURITY;

--
-- Name: ciclo_liquidacion ciclo_liquidacion_select; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY ciclo_liquidacion_select ON public.ciclo_liquidacion FOR SELECT TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: TABLE ciclo_liquidacion; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.ciclo_liquidacion FROM anon, authenticated;

GRANT SELECT ON TABLE public.ciclo_liquidacion TO authenticated;
