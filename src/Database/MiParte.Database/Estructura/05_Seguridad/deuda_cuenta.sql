--
-- Name: deuda_cuenta; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.deuda_cuenta ENABLE ROW LEVEL SECURITY;


--
-- Name: deuda_cuenta deuda_cuenta_hogar; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY deuda_cuenta_hogar ON public.deuda_cuenta TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares))) WITH CHECK ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: TABLE deuda_cuenta; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.deuda_cuenta FROM anon, authenticated;
GRANT SELECT ON TABLE public.deuda_cuenta TO authenticated;
