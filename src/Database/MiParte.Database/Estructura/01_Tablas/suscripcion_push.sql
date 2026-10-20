--
-- Name: suscripcion_push; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.suscripcion_push (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    miembro_id uuid NOT NULL,
    endpoint text NOT NULL,
    p256dh text NOT NULL,
    auth text NOT NULL,
    creada_en timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT suscripcion_push_endpoint_check CHECK ((endpoint ~~ 'https://%'::text)),
    CONSTRAINT suscripcion_push_claves_check CHECK (((p256dh <> ''::text) AND (auth <> ''::text)))
);


--
-- Name: suscripcion_push suscripcion_push_endpoint_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.suscripcion_push
    ADD CONSTRAINT suscripcion_push_endpoint_key UNIQUE (endpoint);


--
-- Name: suscripcion_push suscripcion_push_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.suscripcion_push
    ADD CONSTRAINT suscripcion_push_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: suscripcion_push suscripcion_push_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.suscripcion_push
    ADD CONSTRAINT suscripcion_push_pkey PRIMARY KEY (id);
