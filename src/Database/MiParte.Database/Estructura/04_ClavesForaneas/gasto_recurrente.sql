--
-- Name: gasto_recurrente gasto_recurrente_hogar_id_categoria_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto_recurrente
    ADD CONSTRAINT gasto_recurrente_hogar_id_categoria_id_fkey FOREIGN KEY (hogar_id, categoria_id) REFERENCES public.categoria(hogar_id, id);


--
-- Name: gasto_recurrente gasto_recurrente_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto_recurrente
    ADD CONSTRAINT gasto_recurrente_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: gasto_recurrente gasto_recurrente_hogar_id_pagado_por_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto_recurrente
    ADD CONSTRAINT gasto_recurrente_hogar_id_pagado_por_fkey FOREIGN KEY (hogar_id, pagado_por) REFERENCES public.miembro(hogar_id, id);


--
-- Name: gasto_recurrente gasto_recurrente_hogar_id_perfil_reparto_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto_recurrente
    ADD CONSTRAINT gasto_recurrente_hogar_id_perfil_reparto_id_fkey FOREIGN KEY (hogar_id, perfil_reparto_id) REFERENCES public.perfil_reparto(hogar_id, id);
