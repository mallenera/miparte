--
-- Name: producto_compra; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.producto_compra (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    nombre text NOT NULL,
    cantidad integer DEFAULT 1 NOT NULL,
    comprado boolean DEFAULT false NOT NULL,
    creado_en timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT producto_compra_cantidad_check CHECK (((cantidad >= 1) AND (cantidad <= 999))),
    CONSTRAINT producto_compra_nombre_check CHECK (((length(TRIM(BOTH FROM nombre)) >= 1) AND (length(nombre) <= 100)))
);


--
-- Name: producto_compra producto_compra_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.producto_compra
    ADD CONSTRAINT producto_compra_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: producto_compra producto_compra_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.producto_compra
    ADD CONSTRAINT producto_compra_pkey PRIMARY KEY (id);


--
-- Name: producto_compra_hogar_comprado_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX producto_compra_hogar_comprado_idx ON public.producto_compra USING btree (hogar_id, comprado, creado_en);
