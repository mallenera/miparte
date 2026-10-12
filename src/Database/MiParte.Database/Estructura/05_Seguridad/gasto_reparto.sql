--
-- Name: gasto_reparto; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.gasto_reparto ENABLE ROW LEVEL SECURITY;

--
-- Name: gasto_reparto gasto_reparto_hogar; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY gasto_reparto_hogar ON public.gasto_reparto TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares))) WITH CHECK ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: TABLE gasto_reparto; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.gasto_reparto FROM anon, authenticated;

GRANT MAINTAIN ON TABLE public.gasto_reparto TO anon;
GRANT SELECT,MAINTAIN ON TABLE public.gasto_reparto TO authenticated;
