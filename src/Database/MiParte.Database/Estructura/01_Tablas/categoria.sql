--
-- Name: categoria; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.categoria (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    nombre text NOT NULL,
    categoria_padre_id uuid,
    perfil_reparto_id uuid,
    a_cargo_cuenta_comun boolean DEFAULT false NOT NULL,
    CONSTRAINT categoria_nombre_check CHECK ((length(btrim(nombre)) > 0))
);


--
-- Name: categoria categoria_hogar_id_categoria_padre_id_nombre_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.categoria
    ADD CONSTRAINT categoria_hogar_id_categoria_padre_id_nombre_key UNIQUE NULLS NOT DISTINCT (hogar_id, categoria_padre_id, nombre);


--
-- Name: categoria categoria_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.categoria
    ADD CONSTRAINT categoria_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: categoria categoria_nombre_longitud; Type: CHECK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE public.categoria
    ADD CONSTRAINT categoria_nombre_longitud CHECK ((length(nombre) <= 100)) NOT VALID;


--
-- Name: categoria categoria_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.categoria
    ADD CONSTRAINT categoria_pkey PRIMARY KEY (id);
