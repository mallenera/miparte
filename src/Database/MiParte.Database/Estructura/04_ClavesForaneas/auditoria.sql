--
-- Name: auditoria auditoria_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.auditoria
    ADD CONSTRAINT auditoria_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;
