--
-- Name: enlace_grupo; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.enlace_grupo (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    token_hash text NOT NULL,
    creado_por uuid NOT NULL,
    creado_en timestamp with time zone DEFAULT now() NOT NULL,
    caduca_en timestamp with time zone,
    activo boolean DEFAULT true NOT NULL,
    usos integer DEFAULT 0 NOT NULL,
    CONSTRAINT enlace_grupo_caduca_posterior CHECK (((caduca_en IS NULL) OR (caduca_en > creado_en))),
    CONSTRAINT enlace_grupo_token_hash_check CHECK ((token_hash ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT enlace_grupo_usos_check CHECK ((usos >= 0))
);


--
-- Name: enlace_grupo enlace_grupo_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.enlace_grupo
    ADD CONSTRAINT enlace_grupo_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: enlace_grupo enlace_grupo_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.enlace_grupo
    ADD CONSTRAINT enlace_grupo_pkey PRIMARY KEY (id);


--
-- Name: enlace_grupo enlace_grupo_token_hash_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.enlace_grupo
    ADD CONSTRAINT enlace_grupo_token_hash_key UNIQUE (token_hash);
