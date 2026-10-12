--
-- Name: retirada_ahorro; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.retirada_ahorro (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    miembro_id uuid NOT NULL,
    fecha date NOT NULL,
    importe numeric(12,2) NOT NULL,
    concepto text,
    CONSTRAINT retirada_ahorro_concepto_check CHECK ((length(concepto) <= 200)),
    CONSTRAINT retirada_ahorro_importe_check CHECK ((importe > (0)::numeric))
);


--
-- Name: retirada_ahorro retirada_ahorro_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.retirada_ahorro
    ADD CONSTRAINT retirada_ahorro_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: retirada_ahorro retirada_ahorro_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.retirada_ahorro
    ADD CONSTRAINT retirada_ahorro_pkey PRIMARY KEY (id);


--
-- Name: retirada_ahorro_fecha_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX retirada_ahorro_fecha_idx ON public.retirada_ahorro USING btree (hogar_id, fecha);
