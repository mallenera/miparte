--
-- Name: invitacion_hogar; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.invitacion_hogar ENABLE ROW LEVEL SECURITY;

--
-- Name: invitacion_hogar invitacion_hogar_delete; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY invitacion_hogar_delete ON public.invitacion_hogar FOR DELETE TO authenticated USING (public.es_admin_hogar(hogar_id));


--
-- Name: invitacion_hogar invitacion_hogar_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY invitacion_hogar_insert ON public.invitacion_hogar FOR INSERT TO authenticated WITH CHECK ((public.es_admin_hogar(hogar_id) AND (creada_por = auth.uid())));


--
-- Name: invitacion_hogar invitacion_hogar_select; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY invitacion_hogar_select ON public.invitacion_hogar FOR SELECT TO authenticated USING (public.es_admin_hogar(hogar_id));


--
-- Name: invitacion_hogar invitacion_hogar_update; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY invitacion_hogar_update ON public.invitacion_hogar FOR UPDATE TO authenticated USING (public.es_admin_hogar(hogar_id)) WITH CHECK (public.es_admin_hogar(hogar_id));


--
-- Name: TABLE invitacion_hogar; Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON TABLE public.invitacion_hogar FROM anon, authenticated;

GRANT MAINTAIN ON TABLE public.invitacion_hogar TO anon;
GRANT SELECT,MAINTAIN ON TABLE public.invitacion_hogar TO authenticated;
