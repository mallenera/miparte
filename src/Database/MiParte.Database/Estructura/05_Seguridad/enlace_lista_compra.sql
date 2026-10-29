--
-- Name: enlace_lista_compra; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.enlace_lista_compra ENABLE ROW LEVEL SECURITY;


--
-- Name: enlace_lista_compra enlace_lista_compra_hogar; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY enlace_lista_compra_hogar ON public.enlace_lista_compra TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares))) WITH CHECK ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: TABLE enlace_lista_compra; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.enlace_lista_compra FROM anon, authenticated;
GRANT SELECT ON TABLE public.enlace_lista_compra TO authenticated;
