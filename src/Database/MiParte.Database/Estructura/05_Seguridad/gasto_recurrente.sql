--
-- Name: gasto_recurrente; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.gasto_recurrente ENABLE ROW LEVEL SECURITY;

--
-- Name: gasto_recurrente api_servicio; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY api_servicio ON public.gasto_recurrente TO miparte_api USING (true) WITH CHECK (true);


--
-- Name: gasto_recurrente gasto_recurrente_hogar; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY gasto_recurrente_hogar ON public.gasto_recurrente TO authenticated USING (((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)) AND (NOT es_personal OR (pagado_por = public.mi_miembro_id(hogar_id))))) WITH CHECK (((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)) AND (NOT es_personal OR (pagado_por = public.mi_miembro_id(hogar_id)))));


--
-- Name: TABLE gasto_recurrente; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.gasto_recurrente FROM anon, authenticated, miparte_api;

GRANT SELECT ON TABLE public.gasto_recurrente TO authenticated;
GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE public.gasto_recurrente TO miparte_api;
