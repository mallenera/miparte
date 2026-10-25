--
-- Name: perfil_reparto; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.perfil_reparto (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    nombre text NOT NULL,
    modo text NOT NULL,
    predeterminado_gastos boolean DEFAULT false NOT NULL,
    CONSTRAINT perfil_reparto_modo_check CHECK ((modo = ANY (ARRAY['porcentaje'::text, 'partes'::text, 'cuenta_comun'::text, 'individual'::text, 'cuenta_comun_ahorro'::text]))),
    CONSTRAINT perfil_reparto_nombre_check CHECK ((length(btrim(nombre)) > 0))
);


--
-- Name: perfil_reparto perfil_reparto_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.perfil_reparto
    ADD CONSTRAINT perfil_reparto_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: perfil_reparto perfil_reparto_hogar_id_nombre_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.perfil_reparto
    ADD CONSTRAINT perfil_reparto_hogar_id_nombre_key UNIQUE (hogar_id, nombre);


--
-- Name: perfil_reparto perfil_reparto_nombre_longitud; Type: CHECK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE public.perfil_reparto
    ADD CONSTRAINT perfil_reparto_nombre_longitud CHECK ((length(nombre) <= 100)) NOT VALID;


--
-- Name: perfil_reparto perfil_reparto_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.perfil_reparto
    ADD CONSTRAINT perfil_reparto_pkey PRIMARY KEY (id);


--
-- Name: perfil_reparto_predeterminado_gastos_key; Type: INDEX; Schema: public; Owner: -
-- Como máximo un perfil predeterminado para gastos por hogar.
--

CREATE UNIQUE INDEX perfil_reparto_predeterminado_gastos_key ON public.perfil_reparto USING btree (hogar_id) WHERE predeterminado_gastos;
