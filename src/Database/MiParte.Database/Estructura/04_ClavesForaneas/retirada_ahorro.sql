--
-- Name: retirada_ahorro retirada_ahorro_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.retirada_ahorro
    ADD CONSTRAINT retirada_ahorro_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: retirada_ahorro retirada_ahorro_hogar_id_miembro_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.retirada_ahorro
    ADD CONSTRAINT retirada_ahorro_hogar_id_miembro_id_fkey FOREIGN KEY (hogar_id, miembro_id) REFERENCES public.miembro(hogar_id, id);
