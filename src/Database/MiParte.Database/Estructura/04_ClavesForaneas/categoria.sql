--
-- Name: categoria categoria_hogar_id_categoria_padre_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.categoria
    ADD CONSTRAINT categoria_hogar_id_categoria_padre_id_fkey FOREIGN KEY (hogar_id, categoria_padre_id) REFERENCES public.categoria(hogar_id, id);


--
-- Name: categoria categoria_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.categoria
    ADD CONSTRAINT categoria_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: categoria categoria_hogar_id_perfil_reparto_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.categoria
    ADD CONSTRAINT categoria_hogar_id_perfil_reparto_id_fkey FOREIGN KEY (hogar_id, perfil_reparto_id) REFERENCES public.perfil_reparto(hogar_id, id);
