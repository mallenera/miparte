--
-- Name: perfil_reparto_detalle; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.perfil_reparto_detalle (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    perfil_id uuid NOT NULL,
    miembro_id uuid NOT NULL,
    valor numeric(12,4) NOT NULL,
    CONSTRAINT perfil_reparto_detalle_valor_check CHECK ((valor >= (0)::numeric))
);


--
-- Name: perfil_reparto_detalle perfil_reparto_detalle_perfil_id_miembro_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.perfil_reparto_detalle
    ADD CONSTRAINT perfil_reparto_detalle_perfil_id_miembro_id_key UNIQUE (perfil_id, miembro_id);


--
-- Name: perfil_reparto_detalle perfil_reparto_detalle_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.perfil_reparto_detalle
    ADD CONSTRAINT perfil_reparto_detalle_pkey PRIMARY KEY (id);
