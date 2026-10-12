--
-- Name: mes_cerrado; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.mes_cerrado (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    mes date NOT NULL,
    cerrado_en timestamp with time zone DEFAULT now() NOT NULL,
    cerrado_por uuid,
    CONSTRAINT mes_cerrado_mes_check CHECK ((EXTRACT(day FROM mes) = (1)::numeric))
);


--
-- Name: mes_cerrado mes_cerrado_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mes_cerrado
    ADD CONSTRAINT mes_cerrado_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: mes_cerrado mes_cerrado_hogar_id_mes_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mes_cerrado
    ADD CONSTRAINT mes_cerrado_hogar_id_mes_key UNIQUE (hogar_id, mes);


--
-- Name: mes_cerrado mes_cerrado_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mes_cerrado
    ADD CONSTRAINT mes_cerrado_pkey PRIMARY KEY (id);
