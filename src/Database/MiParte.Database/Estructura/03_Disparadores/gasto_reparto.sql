--
-- Name: gasto_reparto gasto_reparto_mes_abierto; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER gasto_reparto_mes_abierto BEFORE INSERT OR DELETE OR UPDATE ON public.gasto_reparto FOR EACH ROW EXECUTE FUNCTION public.gasto_reparto_mes_abierto();
