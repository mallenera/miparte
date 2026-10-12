--
-- Name: miembro miembro_proteger; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER miembro_proteger BEFORE INSERT OR DELETE OR UPDATE ON public.miembro FOR EACH ROW EXECUTE FUNCTION public.miembro_proteger();
