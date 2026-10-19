--
-- Name: gasto_reparto_a_cargo gasto_reparto_a_cargo_hogar_id_gasto_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto_reparto_a_cargo
    ADD CONSTRAINT gasto_reparto_a_cargo_hogar_id_gasto_id_fkey FOREIGN KEY (hogar_id, gasto_id) REFERENCES public.gasto(hogar_id, id) ON DELETE CASCADE;


--
-- Name: gasto_reparto_a_cargo gasto_reparto_a_cargo_hogar_id_miembro_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto_reparto_a_cargo
    ADD CONSTRAINT gasto_reparto_a_cargo_hogar_id_miembro_id_fkey FOREIGN KEY (hogar_id, miembro_id) REFERENCES public.miembro(hogar_id, id);


--
-- Name: gasto_reparto_a_cargo gasto_reparto_a_cargo_hogar_id_responsable_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto_reparto_a_cargo
    ADD CONSTRAINT gasto_reparto_a_cargo_hogar_id_responsable_id_fkey FOREIGN KEY (hogar_id, responsable_id) REFERENCES public.miembro(hogar_id, id);
