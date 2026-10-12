--
-- Name: gasto_reparto; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.gasto_reparto (
    gasto_id uuid NOT NULL,
    miembro_id uuid NOT NULL,
    hogar_id uuid NOT NULL,
    importe_asumido numeric(12,2) NOT NULL,
    CONSTRAINT gasto_reparto_importe_asumido_check CHECK ((importe_asumido >= (0)::numeric))
);


--
-- Name: gasto_reparto gasto_reparto_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto_reparto
    ADD CONSTRAINT gasto_reparto_pkey PRIMARY KEY (gasto_id, miembro_id);


--
-- Name: gasto_reparto_miembro; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX gasto_reparto_miembro ON public.gasto_reparto USING btree (hogar_id, miembro_id);
