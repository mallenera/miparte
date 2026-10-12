--
-- Name: mes_cerrado mes_cerrado_bloquear; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER mes_cerrado_bloquear BEFORE INSERT ON public.mes_cerrado FOR EACH ROW EXECUTE FUNCTION public.mes_cerrado_bloquear();
