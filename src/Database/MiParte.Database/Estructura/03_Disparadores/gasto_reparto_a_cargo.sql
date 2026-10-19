--
-- Name: gasto_reparto_a_cargo gasto_reparto_a_cargo_mes_abierto; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER gasto_reparto_a_cargo_mes_abierto BEFORE INSERT OR DELETE OR UPDATE ON public.gasto_reparto_a_cargo FOR EACH ROW EXECUTE FUNCTION public.gasto_reparto_mes_abierto();
