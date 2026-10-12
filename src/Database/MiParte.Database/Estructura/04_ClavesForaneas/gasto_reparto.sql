--
-- Name: gasto_reparto gasto_reparto_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto_reparto
    ADD CONSTRAINT gasto_reparto_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: gasto_reparto gasto_reparto_hogar_id_gasto_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto_reparto
    ADD CONSTRAINT gasto_reparto_hogar_id_gasto_id_fkey FOREIGN KEY (hogar_id, gasto_id) REFERENCES public.gasto(hogar_id, id) ON DELETE CASCADE;


--
-- Name: gasto_reparto gasto_reparto_hogar_id_miembro_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto_reparto
    ADD CONSTRAINT gasto_reparto_hogar_id_miembro_id_fkey FOREIGN KEY (hogar_id, miembro_id) REFERENCES public.miembro(hogar_id, id);
