--
-- Name: gasto_reparto_a_cargo; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.gasto_reparto_a_cargo ENABLE ROW LEVEL SECURITY;

--
-- Name: gasto_reparto_a_cargo api_servicio; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY api_servicio ON public.gasto_reparto_a_cargo TO miparte_api USING (true) WITH CHECK (true);


--
-- Name: gasto_reparto_a_cargo gasto_reparto_a_cargo_hogar; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY gasto_reparto_a_cargo_hogar ON public.gasto_reparto_a_cargo TO authenticated USING (((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)) AND (EXISTS ( SELECT 1 FROM public.gasto g WHERE (g.id = gasto_reparto_a_cargo.gasto_id))))) WITH CHECK ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: TABLE gasto_reparto_a_cargo; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.gasto_reparto_a_cargo FROM anon, authenticated, miparte_api;

GRANT SELECT ON TABLE public.gasto_reparto_a_cargo TO authenticated;
GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE public.gasto_reparto_a_cargo TO miparte_api;
