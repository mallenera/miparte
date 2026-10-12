--
-- Name: auditoria; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.auditoria ENABLE ROW LEVEL SECURITY;

--
-- Name: auditoria auditoria_select; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY auditoria_select ON public.auditoria FOR SELECT TO authenticated USING (public.es_admin_hogar(hogar_id));


--
-- Name: TABLE auditoria; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.auditoria FROM anon, authenticated;

GRANT SELECT ON TABLE public.auditoria TO authenticated;
