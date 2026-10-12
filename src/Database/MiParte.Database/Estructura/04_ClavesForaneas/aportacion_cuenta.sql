--
-- Name: aportacion_cuenta aportacion_cuenta_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aportacion_cuenta
    ADD CONSTRAINT aportacion_cuenta_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: aportacion_cuenta aportacion_cuenta_hogar_id_miembro_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aportacion_cuenta
    ADD CONSTRAINT aportacion_cuenta_hogar_id_miembro_id_fkey FOREIGN KEY (hogar_id, miembro_id) REFERENCES public.miembro(hogar_id, id);
