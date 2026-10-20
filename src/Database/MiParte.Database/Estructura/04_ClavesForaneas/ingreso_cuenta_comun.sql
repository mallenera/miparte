--
-- Name: ingreso_cuenta_comun ingreso_cuenta_comun_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ingreso_cuenta_comun
    ADD CONSTRAINT ingreso_cuenta_comun_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: ingreso_cuenta_comun ingreso_cuenta_comun_hogar_id_miembro_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ingreso_cuenta_comun
    ADD CONSTRAINT ingreso_cuenta_comun_hogar_id_miembro_id_fkey FOREIGN KEY (hogar_id, miembro_id) REFERENCES public.miembro(hogar_id, id);
