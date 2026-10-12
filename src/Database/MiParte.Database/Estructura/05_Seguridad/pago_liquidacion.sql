--
-- Name: pago_liquidacion; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.pago_liquidacion ENABLE ROW LEVEL SECURITY;

--
-- Name: pago_liquidacion pago_liquidacion_hogar; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY pago_liquidacion_hogar ON public.pago_liquidacion TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares))) WITH CHECK ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: TABLE pago_liquidacion; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.pago_liquidacion FROM anon, authenticated;

GRANT SELECT ON TABLE public.pago_liquidacion TO authenticated;
