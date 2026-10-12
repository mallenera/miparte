--
-- Name: miembro miembro_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.miembro
    ADD CONSTRAINT miembro_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: miembro miembro_hogar_id_responsable_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.miembro
    ADD CONSTRAINT miembro_hogar_id_responsable_id_fkey FOREIGN KEY (hogar_id, responsable_id) REFERENCES public.miembro(hogar_id, id);


--
-- Name: miembro miembro_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.miembro
    ADD CONSTRAINT miembro_user_id_fkey FOREIGN KEY (user_id) REFERENCES auth.users(id) ON DELETE SET NULL;
