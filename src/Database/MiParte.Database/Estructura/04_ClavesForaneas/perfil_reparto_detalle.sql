--
-- Name: perfil_reparto_detalle perfil_reparto_detalle_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.perfil_reparto_detalle
    ADD CONSTRAINT perfil_reparto_detalle_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: perfil_reparto_detalle perfil_reparto_detalle_hogar_id_miembro_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.perfil_reparto_detalle
    ADD CONSTRAINT perfil_reparto_detalle_hogar_id_miembro_id_fkey FOREIGN KEY (hogar_id, miembro_id) REFERENCES public.miembro(hogar_id, id);


--
-- Name: perfil_reparto_detalle perfil_reparto_detalle_hogar_id_perfil_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.perfil_reparto_detalle
    ADD CONSTRAINT perfil_reparto_detalle_hogar_id_perfil_id_fkey FOREIGN KEY (hogar_id, perfil_id) REFERENCES public.perfil_reparto(hogar_id, id) ON DELETE CASCADE;
