--
-- Name: gasto gasto_mes_abierto; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER gasto_mes_abierto BEFORE INSERT OR DELETE OR UPDATE ON public.gasto FOR EACH ROW EXECUTE FUNCTION public.gasto_mes_abierto();
