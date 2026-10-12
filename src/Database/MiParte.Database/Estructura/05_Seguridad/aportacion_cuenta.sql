--
-- Name: aportacion_cuenta; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.aportacion_cuenta ENABLE ROW LEVEL SECURITY;

--
-- Name: aportacion_cuenta aportacion_cuenta_hogar; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY aportacion_cuenta_hogar ON public.aportacion_cuenta TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares))) WITH CHECK ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: TABLE aportacion_cuenta; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.aportacion_cuenta FROM anon, authenticated;

GRANT MAINTAIN ON TABLE public.aportacion_cuenta TO anon;
GRANT SELECT,MAINTAIN ON TABLE public.aportacion_cuenta TO authenticated;
