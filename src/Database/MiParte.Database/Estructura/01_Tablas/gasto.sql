--
-- Name: gasto; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.gasto (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    fecha date NOT NULL,
    importe numeric(12,2) NOT NULL,
    categoria_id uuid NOT NULL,
    pagado_por uuid,
    perfil_reparto_id uuid NOT NULL,
    concepto text,
    gasto_recurrente_id uuid,
    a_cargo_cuenta_comun boolean DEFAULT false NOT NULL,
    pagado_desde_ahorro boolean DEFAULT false NOT NULL,
    es_personal boolean DEFAULT false NOT NULL,
    ciclo_liquidacion_id uuid,
    CONSTRAINT gasto_es_personal_check CHECK (((NOT es_personal) OR ((pagado_por IS NOT NULL) AND (NOT a_cargo_cuenta_comun) AND (NOT pagado_desde_ahorro)))),
    CONSTRAINT gasto_importe_check CHECK ((importe > (0)::numeric)),
    CONSTRAINT gasto_pagado_desde_ahorro_check CHECK (((NOT pagado_desde_ahorro) OR ((pagado_por IS NULL) AND a_cargo_cuenta_comun))),
    CONSTRAINT gasto_pagado_por_cuenta_comun_check CHECK (((pagado_por IS NOT NULL) OR a_cargo_cuenta_comun))
);


--
-- Name: gasto gasto_concepto_longitud; Type: CHECK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE public.gasto
    ADD CONSTRAINT gasto_concepto_longitud CHECK ((length(concepto) <= 200)) NOT VALID;


--
-- Name: gasto gasto_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto
    ADD CONSTRAINT gasto_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: gasto gasto_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto
    ADD CONSTRAINT gasto_pkey PRIMARY KEY (id);


--
-- Name: gasto_ahorro_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX gasto_ahorro_idx ON public.gasto USING btree (hogar_id, fecha) INCLUDE (importe) WHERE pagado_desde_ahorro;


--
-- Name: gasto_categoria_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX gasto_categoria_idx ON public.gasto USING btree (hogar_id, categoria_id);


--
-- Name: gasto_cuenta_comun_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX gasto_cuenta_comun_idx ON public.gasto USING btree (hogar_id, fecha) INCLUDE (importe, pagado_por) WHERE a_cargo_cuenta_comun;


--
-- Name: gasto_mes_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX gasto_mes_idx ON public.gasto USING btree (hogar_id, fecha);


--
-- Name: gasto_recurrente_mes_uq; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX gasto_recurrente_mes_uq ON public.gasto USING btree (hogar_id, gasto_recurrente_id, ((date_trunc('month'::text, (fecha)::timestamp without time zone))::date)) WHERE (gasto_recurrente_id IS NOT NULL);
