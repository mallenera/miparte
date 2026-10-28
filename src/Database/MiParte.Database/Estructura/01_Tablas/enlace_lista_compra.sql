--
-- Name: enlace_lista_compra; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.enlace_lista_compra (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    token_hash text NOT NULL,
    creado_por uuid NOT NULL,
    creado_en timestamp with time zone DEFAULT now() NOT NULL,
    caduca_en timestamp with time zone,
    activo boolean DEFAULT true NOT NULL
);


--
-- Name: enlace_lista_compra enlace_lista_compra_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.enlace_lista_compra
    ADD CONSTRAINT enlace_lista_compra_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: enlace_lista_compra enlace_lista_compra_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.enlace_lista_compra
    ADD CONSTRAINT enlace_lista_compra_pkey PRIMARY KEY (id);


--
-- Name: enlace_lista_compra_token_hash_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.enlace_lista_compra
    ADD CONSTRAINT enlace_lista_compra_token_hash_key UNIQUE (token_hash);
