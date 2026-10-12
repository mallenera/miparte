--
-- Name: invitacion_hogar invitacion_hogar_creada_por_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invitacion_hogar
    ADD CONSTRAINT invitacion_hogar_creada_por_fkey FOREIGN KEY (creada_por) REFERENCES auth.users(id) ON DELETE CASCADE;


--
-- Name: invitacion_hogar invitacion_hogar_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invitacion_hogar
    ADD CONSTRAINT invitacion_hogar_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: invitacion_hogar invitacion_hogar_hogar_id_miembro_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invitacion_hogar
    ADD CONSTRAINT invitacion_hogar_hogar_id_miembro_id_fkey FOREIGN KEY (hogar_id, miembro_id) REFERENCES public.miembro(hogar_id, id) ON DELETE CASCADE;


--
-- Name: invitacion_hogar invitacion_hogar_usada_por_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invitacion_hogar
    ADD CONSTRAINT invitacion_hogar_usada_por_fkey FOREIGN KEY (usada_por) REFERENCES auth.users(id) ON DELETE SET NULL;
