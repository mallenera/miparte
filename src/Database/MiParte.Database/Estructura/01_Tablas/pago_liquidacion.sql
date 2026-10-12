--
-- Name: pago_liquidacion; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.pago_liquidacion (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    mes date NOT NULL,
    de_miembro_id uuid NOT NULL,
    a_miembro_id uuid NOT NULL,
    importe numeric(12,2) NOT NULL,
    fecha date NOT NULL,
    concepto text,
    CONSTRAINT pago_liquidacion_check CHECK ((de_miembro_id <> a_miembro_id)),
    CONSTRAINT pago_liquidacion_importe_check CHECK ((importe > (0)::numeric)),
    CONSTRAINT pago_liquidacion_mes_check CHECK ((EXTRACT(day FROM mes) = (1)::numeric))
);


--
-- Name: pago_liquidacion pago_liquidacion_concepto_longitud; Type: CHECK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE public.pago_liquidacion
    ADD CONSTRAINT pago_liquidacion_concepto_longitud CHECK ((length(concepto) <= 200)) NOT VALID;


--
-- Name: pago_liquidacion pago_liquidacion_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.pago_liquidacion
    ADD CONSTRAINT pago_liquidacion_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: pago_liquidacion pago_liquidacion_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.pago_liquidacion
    ADD CONSTRAINT pago_liquidacion_pkey PRIMARY KEY (id);


--
-- Name: pago_liquidacion_mes_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX pago_liquidacion_mes_idx ON public.pago_liquidacion USING btree (hogar_id, mes);
