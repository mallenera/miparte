--
-- Name: gasto_recurrente; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.gasto_recurrente ENABLE ROW LEVEL SECURITY;

--
-- Name: gasto_recurrente gasto_recurrente_hogar; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY gasto_recurrente_hogar ON public.gasto_recurrente TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares))) WITH CHECK ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: TABLE gasto_recurrente; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.gasto_recurrente FROM anon, authenticated;

GRANT MAINTAIN ON TABLE public.gasto_recurrente TO anon;
GRANT SELECT,MAINTAIN ON TABLE public.gasto_recurrente TO authenticated;
