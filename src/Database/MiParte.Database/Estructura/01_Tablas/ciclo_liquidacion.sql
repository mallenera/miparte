--
-- Name: ciclo_liquidacion; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ciclo_liquidacion (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    numero integer NOT NULL,
    cerrado_en timestamp with time zone DEFAULT now() NOT NULL,
    cerrado_por uuid NOT NULL,
    desde_gasto_fecha date NOT NULL,
    hasta_fecha date NOT NULL,
    total numeric(12,2) NOT NULL,
    CONSTRAINT ciclo_liquidacion_fechas_check CHECK ((desde_gasto_fecha <= hasta_fecha)),
    CONSTRAINT ciclo_liquidacion_numero_check CHECK ((numero > 0)),
    CONSTRAINT ciclo_liquidacion_total_check CHECK ((total > (0)::numeric))
);


--
-- Name: ciclo_liquidacion ciclo_liquidacion_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ciclo_liquidacion
    ADD CONSTRAINT ciclo_liquidacion_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: ciclo_liquidacion ciclo_liquidacion_hogar_id_numero_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ciclo_liquidacion
    ADD CONSTRAINT ciclo_liquidacion_hogar_id_numero_key UNIQUE (hogar_id, numero);


--
-- Name: ciclo_liquidacion ciclo_liquidacion_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ciclo_liquidacion
    ADD CONSTRAINT ciclo_liquidacion_pkey PRIMARY KEY (id);
