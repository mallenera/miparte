--
-- Name: auditoria auditoria_inmutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER auditoria_inmutable BEFORE DELETE OR UPDATE ON public.auditoria FOR EACH ROW EXECUTE FUNCTION public.auditoria_inmutable();


--
-- Name: auditoria auditoria_sin_truncate; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER auditoria_sin_truncate BEFORE TRUNCATE ON public.auditoria FOR EACH STATEMENT EXECUTE FUNCTION public.auditoria_sin_truncate();
