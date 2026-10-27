--
-- Name: suscripcion_push; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.suscripcion_push ENABLE ROW LEVEL SECURITY;

--
-- Name: suscripcion_push api_servicio; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY api_servicio ON public.suscripcion_push TO miparte_api USING (true) WITH CHECK (true);


--
-- Name: suscripcion_push suscripcion_push_propia; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY suscripcion_push_propia ON public.suscripcion_push TO authenticated USING ((miembro_id IN ( SELECT m.id FROM public.miembro m WHERE (m.user_id = auth.uid())))) WITH CHECK ((miembro_id IN ( SELECT m.id FROM public.miembro m WHERE (m.user_id = auth.uid()))));


--
-- Name: TABLE suscripcion_push; Type: ACL; Schema: public; Owner: -
--

-- Los endpoints y claves de push solo los toca Core.Api (rol miparte_api); PostgREST no los expone.
REVOKE ALL ON TABLE public.suscripcion_push FROM anon, authenticated, miparte_api;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE public.suscripcion_push TO miparte_api;
