--
-- Name: pago_liquidacion pago_liquidacion_hogar_id_a_miembro_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.pago_liquidacion
    ADD CONSTRAINT pago_liquidacion_hogar_id_a_miembro_id_fkey FOREIGN KEY (hogar_id, a_miembro_id) REFERENCES public.miembro(hogar_id, id);


--
-- Name: pago_liquidacion pago_liquidacion_hogar_id_de_miembro_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.pago_liquidacion
    ADD CONSTRAINT pago_liquidacion_hogar_id_de_miembro_id_fkey FOREIGN KEY (hogar_id, de_miembro_id) REFERENCES public.miembro(hogar_id, id);


--
-- Name: pago_liquidacion pago_liquidacion_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.pago_liquidacion
    ADD CONSTRAINT pago_liquidacion_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;
