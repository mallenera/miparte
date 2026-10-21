--
-- Name: auditoria; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.auditoria (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    usuario_id uuid,
    cuando timestamp with time zone DEFAULT now() NOT NULL,
    accion text NOT NULL,
    entidad text NOT NULL,
    entidad_id uuid NOT NULL,
    antes jsonb,
    despues jsonb,
    personal_de uuid,
    origen text DEFAULT 'manual'::text NOT NULL,
    CONSTRAINT auditoria_accion_check CHECK ((accion = ANY (ARRAY['crear'::text, 'editar'::text, 'borrar'::text, 'vincular'::text, 'usar'::text]))),
    CONSTRAINT auditoria_origen_check CHECK ((origen = ANY (ARRAY['manual'::text, 'asistente'::text]))),
    CONSTRAINT auditoria_check CHECK (((antes IS NOT NULL) OR (despues IS NOT NULL))),
    CONSTRAINT auditoria_entidad_check CHECK (((length(entidad) >= 1) AND (length(entidad) <= 40)))
);


--
-- Name: auditoria auditoria_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.auditoria
    ADD CONSTRAINT auditoria_pkey PRIMARY KEY (id);


--
-- Name: auditoria_entidad_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX auditoria_entidad_idx ON public.auditoria USING btree (hogar_id, entidad, entidad_id);


--
-- Name: auditoria_hogar_cuando_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX auditoria_hogar_cuando_idx ON public.auditoria USING btree (hogar_id, cuando DESC);
