--
-- Name: gasto; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.gasto ENABLE ROW LEVEL SECURITY;

--
-- Name: gasto api_servicio; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY api_servicio ON public.gasto TO miparte_api USING (true) WITH CHECK (true);


--
-- Name: gasto gasto_hogar; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY gasto_hogar ON public.gasto TO authenticated USING (((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)) AND (NOT es_personal OR (pagado_por = public.mi_miembro_id(hogar_id))))) WITH CHECK (((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)) AND (NOT es_personal OR (pagado_por = public.mi_miembro_id(hogar_id)))));


--
-- Name: TABLE gasto; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.gasto FROM anon, authenticated, miparte_api;

GRANT SELECT ON TABLE public.gasto TO authenticated;
GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE public.gasto TO miparte_api;
