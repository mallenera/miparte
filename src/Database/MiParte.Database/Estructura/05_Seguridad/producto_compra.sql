--
-- Name: producto_compra; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.producto_compra ENABLE ROW LEVEL SECURITY;


--
-- Name: producto_compra api_servicio; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY api_servicio ON public.producto_compra TO miparte_api USING (true) WITH CHECK (true);


--
-- Name: producto_compra producto_compra_hogar; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY producto_compra_hogar ON public.producto_compra TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares))) WITH CHECK ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: TABLE producto_compra; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.producto_compra FROM anon, authenticated, miparte_api;
GRANT SELECT ON TABLE public.producto_compra TO authenticated;
GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE public.producto_compra TO miparte_api;
