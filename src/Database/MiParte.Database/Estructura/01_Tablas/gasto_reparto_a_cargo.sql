--
-- Name: gasto_reparto_a_cargo; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.gasto_reparto_a_cargo (
    gasto_id uuid NOT NULL,
    miembro_id uuid NOT NULL,
    responsable_id uuid NOT NULL,
    hogar_id uuid NOT NULL,
    importe_asumido numeric(12,2) NOT NULL,
    CONSTRAINT gasto_reparto_a_cargo_importe_check CHECK ((importe_asumido >= (0)::numeric))
);


--
-- Name: gasto_reparto_a_cargo gasto_reparto_a_cargo_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto_reparto_a_cargo
    ADD CONSTRAINT gasto_reparto_a_cargo_pkey PRIMARY KEY (gasto_id, miembro_id);


--
-- Name: gasto_reparto_a_cargo_responsable; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX gasto_reparto_a_cargo_responsable ON public.gasto_reparto_a_cargo USING btree (hogar_id, responsable_id);
