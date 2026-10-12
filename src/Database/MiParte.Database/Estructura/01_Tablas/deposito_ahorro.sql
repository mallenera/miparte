--
-- Name: deposito_ahorro; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.deposito_ahorro (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    miembro_id uuid NOT NULL,
    fecha date NOT NULL,
    importe numeric(12,2) NOT NULL,
    concepto text,
    CONSTRAINT deposito_ahorro_concepto_check CHECK ((length(concepto) <= 200)),
    CONSTRAINT deposito_ahorro_importe_check CHECK ((importe > (0)::numeric))
);


--
-- Name: deposito_ahorro deposito_ahorro_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.deposito_ahorro
    ADD CONSTRAINT deposito_ahorro_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: deposito_ahorro deposito_ahorro_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.deposito_ahorro
    ADD CONSTRAINT deposito_ahorro_pkey PRIMARY KEY (id);


--
-- Name: deposito_ahorro_fecha_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX deposito_ahorro_fecha_idx ON public.deposito_ahorro USING btree (hogar_id, fecha);
