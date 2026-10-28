--
-- Name: enlace_lista_compra enlace_lista_compra_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.enlace_lista_compra
    ADD CONSTRAINT enlace_lista_compra_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: enlace_lista_compra enlace_lista_compra_hogar_id_creado_por_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.enlace_lista_compra
    ADD CONSTRAINT enlace_lista_compra_hogar_id_creado_por_fkey FOREIGN KEY (hogar_id, creado_por) REFERENCES public.miembro(hogar_id, id);
