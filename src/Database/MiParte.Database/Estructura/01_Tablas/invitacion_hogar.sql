--
-- Name: invitacion_hogar; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.invitacion_hogar (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    miembro_id uuid,
    token_hash text NOT NULL,
    creada_por uuid DEFAULT auth.uid() NOT NULL,
    creada_en timestamp with time zone DEFAULT now() NOT NULL,
    caduca_en timestamp with time zone NOT NULL,
    usada_en timestamp with time zone,
    usada_por uuid,
    CONSTRAINT invitacion_hogar_check CHECK ((caduca_en > creada_en)),
    CONSTRAINT invitacion_hogar_token_hash_check CHECK ((token_hash ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT invitacion_hogar_usada_coherente CHECK (((usada_por IS NULL) OR (usada_en IS NOT NULL)))
);


--
-- Name: invitacion_hogar invitacion_hogar_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invitacion_hogar
    ADD CONSTRAINT invitacion_hogar_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: invitacion_hogar invitacion_hogar_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invitacion_hogar
    ADD CONSTRAINT invitacion_hogar_pkey PRIMARY KEY (id);


--
-- Name: invitacion_hogar invitacion_hogar_token_hash_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invitacion_hogar
    ADD CONSTRAINT invitacion_hogar_token_hash_key UNIQUE (token_hash);


--
-- Name: invitacion_hogar_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX invitacion_hogar_idx ON public.invitacion_hogar USING btree (hogar_id);
