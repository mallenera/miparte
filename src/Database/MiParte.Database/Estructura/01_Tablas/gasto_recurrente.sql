--
-- Name: gasto_recurrente; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.gasto_recurrente (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    importe numeric(12,2) NOT NULL,
    categoria_id uuid NOT NULL,
    pagado_por uuid NOT NULL,
    perfil_reparto_id uuid NOT NULL,
    dia_mes smallint NOT NULL,
    concepto text,
    activo boolean DEFAULT true NOT NULL,
    CONSTRAINT gasto_recurrente_dia_mes_check CHECK (((dia_mes >= 1) AND (dia_mes <= 28))),
    CONSTRAINT gasto_recurrente_importe_check CHECK ((importe > (0)::numeric))
);


--
-- Name: gasto_recurrente gasto_recurrente_concepto_longitud; Type: CHECK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE public.gasto_recurrente
    ADD CONSTRAINT gasto_recurrente_concepto_longitud CHECK ((length(concepto) <= 200)) NOT VALID;


--
-- Name: gasto_recurrente gasto_recurrente_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto_recurrente
    ADD CONSTRAINT gasto_recurrente_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: gasto_recurrente gasto_recurrente_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto_recurrente
    ADD CONSTRAINT gasto_recurrente_pkey PRIMARY KEY (id);
