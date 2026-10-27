--
-- Name: auditoria; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.auditoria ENABLE ROW LEVEL SECURITY;

--
-- Name: auditoria api_servicio; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY api_servicio ON public.auditoria TO miparte_api USING (true) WITH CHECK (true);


--
-- Name: auditoria auditoria_select; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY auditoria_select ON public.auditoria FOR SELECT TO authenticated USING ((public.es_admin_hogar(hogar_id) AND ((personal_de IS NULL) OR (personal_de = public.mi_miembro_id(hogar_id)))));


--
-- Name: TABLE auditoria; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.auditoria FROM anon, authenticated, miparte_api;

GRANT SELECT ON TABLE public.auditoria TO authenticated;
GRANT SELECT,INSERT ON TABLE public.auditoria TO miparte_api;
