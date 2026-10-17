--
-- Name: ciclo_liquidacion ciclo_liquidacion_hogar_id_cerrado_por_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ciclo_liquidacion
    ADD CONSTRAINT ciclo_liquidacion_hogar_id_cerrado_por_fkey FOREIGN KEY (hogar_id, cerrado_por) REFERENCES public.miembro(hogar_id, id);


--
-- Name: ciclo_liquidacion ciclo_liquidacion_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ciclo_liquidacion
    ADD CONSTRAINT ciclo_liquidacion_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;
