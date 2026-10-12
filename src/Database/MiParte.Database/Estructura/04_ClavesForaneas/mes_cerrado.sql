--
-- Name: mes_cerrado mes_cerrado_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mes_cerrado
    ADD CONSTRAINT mes_cerrado_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;
