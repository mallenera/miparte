-- Cuenta demo real: usuario «demo» / contraseña «demo» (la app traduce «demo» a demo@miparte.example) y un hogar
-- de ejemplo con dos adultos, una hija a cargo, los cuatro perfiles de siempre y dos meses de gastos.
--
-- Es una SEMILLA, no una migración: no está en supabase/migrations y CI no la aplica. Define la función
-- public.restablecer_demo() y la ejecuta. Es idempotente: cada ejecución borra el hogar demo y lo recrea con datos
-- nuevos fechados respecto a hoy, así que sirve también para RESTABLECER la demo después de que alguien la haya
-- modificado. demo_cron.sql la programa cada día con pg_cron.
--
-- La contraseña «demo» es pública a propósito. Es más corta que el mínimo de Supabase para registrarse, pero el
-- inicio de sesión no la valida, por lo que se escribe aquí directamente su hash. La cuenta solo ve el hogar demo:
-- la RLS y el filtro por hogar de Core.Api la aíslan del resto de datos.
--
-- Los datos reflejan los del modo demo local del front (src/Web/Demo/ServidorDemo.Datos.cs); si cambias unos,
-- cambia los otros.

create or replace function public.restablecer_demo()
returns void
language plpgsql
set search_path = public, extensions
as $$
declare
    v_user    constant uuid := '00000000-0000-4000-8000-00000000de30';
    v_email   constant text := 'demo@miparte.example';
    v_hogar   constant uuid := '00000000-0000-4000-8000-000000000001';

    v_ana     constant uuid := '00000000-0000-4000-8000-000000000010';
    v_marcos  constant uuid := '00000000-0000-4000-8000-000000000011';
    v_lucia   constant uuid := '00000000-0000-4000-8000-000000000012';

    p_cuenta  constant uuid := '00000000-0000-4000-8000-000000000020';
    p_partes  constant uuid := '00000000-0000-4000-8000-000000000021';
    p_porc    constant uuid := '00000000-0000-4000-8000-000000000022';
    p_indiv   constant uuid := '00000000-0000-4000-8000-000000000023';

    c_hipoteca constant uuid := '00000000-0000-4000-8000-000000000030';
    c_alim     constant uuid := '00000000-0000-4000-8000-000000000031';
    c_sumin    constant uuid := '00000000-0000-4000-8000-000000000032';
    c_varios   constant uuid := '00000000-0000-4000-8000-000000000033';
    c_hijo     constant uuid := '00000000-0000-4000-8000-000000000034';
    c_ocio     constant uuid := '00000000-0000-4000-8000-000000000035';
    c_super    constant uuid := '00000000-0000-4000-8000-000000000036';

    r_hipoteca constant uuid := '00000000-0000-4000-8000-000000000040';
    r_internet constant uuid := '00000000-0000-4000-8000-000000000041';

    v_actual   date := date_trunc('month', current_date)::date;
    v_anterior date := (date_trunc('month', current_date) - interval '1 month')::date;
    g          record;
    v_gasto    uuid;
    v_ana_imp  numeric(12,2);
begin
    -- ───── Usuario de Supabase Auth ─────
    -- Se borra y se recrea para que la contraseña y la identidad siempre queden como se espera.
    delete from auth.users where id = v_user;
    insert into auth.users (
        instance_id, id, aud, role, email, encrypted_password, email_confirmed_at,
        raw_app_meta_data, raw_user_meta_data, created_at, updated_at,
        confirmation_token, recovery_token, email_change_token_new, email_change,
        email_change_token_current, phone_change, phone_change_token, reauthentication_token)
    values (
        '00000000-0000-0000-0000-000000000000', v_user, 'authenticated', 'authenticated', v_email,
        crypt('demo', gen_salt('bf')), now(),
        '{"provider":"email","providers":["email"]}', '{"email_verified":true}', now(), now(),
        '', '', '', '', '', '', '', '');
    insert into auth.identities (provider_id, user_id, identity_data, provider, last_sign_in_at, created_at, updated_at)
    values (v_user::text, v_user,
            jsonb_build_object('sub', v_user::text, 'email', v_email, 'email_verified', true),
            'email', now(), now(), now());

    -- ───── Hogar demo (se recrea entero: el borrado en cascada limpia todo lo demás) ─────
    delete from public.hogar where id = v_hogar;
    -- La demo usa la cuenta común (aportaciones y gastos a su cargo), así que el hogar la tiene activada.
    insert into public.hogar (id, nombre, cuenta_comun_activa) values (v_hogar, 'Casa de Ana y Marcos', true);

    insert into public.miembro (id, hogar_id, nombre, tipo, responsable_id, user_id, rol) values
        (v_ana,    v_hogar, 'Ana',    'adulto', null,  v_user, 'admin'),
        (v_marcos, v_hogar, 'Marcos', 'adulto', null,  null,   'miembro');
    insert into public.miembro (id, hogar_id, nombre, tipo, responsable_id) values
        (v_lucia,  v_hogar, 'Lucía',  'a_cargo', v_ana);

    insert into public.perfil_reparto (id, hogar_id, nombre, modo) values
        (p_cuenta, v_hogar, 'Cuenta común',   'cuenta_comun'),
        (p_partes, v_hogar, 'Por partes',     'partes'),
        (p_porc,   v_hogar, 'Porcentaje fijo', 'porcentaje'),
        (p_indiv,  v_hogar, 'Individual',     'individual');
    insert into public.perfil_reparto_detalle (hogar_id, perfil_id, miembro_id, valor) values
        (v_hogar, p_partes, v_ana, 3), (v_hogar, p_partes, v_marcos, 2),
        (v_hogar, p_porc,   v_ana, 50), (v_hogar, p_porc,  v_marcos, 50);

    insert into public.categoria (id, hogar_id, nombre, categoria_padre_id, perfil_reparto_id) values
        (c_hipoteca, v_hogar, 'Hipoteca/Alquiler',      null,    p_partes),
        (c_alim,     v_hogar, 'Alimentación',           null,    p_partes),
        (c_sumin,    v_hogar, 'Suministros',            null,    p_partes),
        (c_varios,   v_hogar, 'Gastos varios de casa',  null,    p_partes),
        (c_hijo,     v_hogar, 'Hijo',                   null,    p_partes),
        (c_ocio,     v_hogar, 'Ocio personal',          null,    p_indiv);
    insert into public.categoria (id, hogar_id, nombre, categoria_padre_id, perfil_reparto_id) values
        (c_super,    v_hogar, 'Supermercado',           c_alim,  p_partes);

    insert into public.gasto_recurrente (id, hogar_id, importe, categoria_id, pagado_por, perfil_reparto_id, dia_mes, concepto) values
        (r_hipoteca, v_hogar, 780.00, c_hipoteca, v_ana,    p_partes, 1, 'Hipoteca'),
        (r_internet, v_hogar,  39.90, c_sumin,    v_marcos, p_partes, 8, 'Internet y móvil');

    -- ───── Gastos: mes anterior completo y mes actual hasta hoy ─────
    -- (mes, día, importe, categoría, pagador, perfil, concepto, plantilla); los futuros se omiten.
    for g in
        select m.mes, x.dia, x.importe, x.categoria, x.pagador, x.perfil, x.concepto, x.plantilla
        from (values (v_anterior, 0), (v_actual, 1)) as m(mes, v)
        cross join lateral (values
            (1,  780.00::numeric,                                   c_hipoteca, v_ana,    p_partes, 'Hipoteca'::text,        r_hipoteca),
            (3,  case m.v when 0 then 86.43 else 79.80 end,         c_super,    v_marcos, p_partes, 'Compra semanal',        null::uuid),
            (6,  case m.v when 0 then 62.15 else 58.40 end,         c_sumin,    v_ana,    p_partes, 'Luz',                   null),
            (8,  39.90,                                             c_sumin,    v_marcos, p_partes, 'Internet y móvil',      r_internet),
            (10, case m.v when 0 then 28.00 else 35.00 end,         c_ocio,     case m.v when 0 then v_marcos else v_ana end,
                                                                                          p_indiv,  case m.v when 0 then 'Cine' else 'Cena con amigos' end, null),
            (12, case m.v when 0 then 74.20 else 102.35 end,        c_super,    v_ana,    p_partes, 'Supermercado',          null),
            (15, 45.60,                                             c_varios,   v_ana,    p_porc,   'Droguería',             null),
            (18, 120.00,                                            c_hijo,     v_ana,    p_partes, 'Material escolar',      null),
            (22, 91.05,                                             c_super,    v_marcos, p_partes, 'Compra del mes',        null)
        ) as x(dia, importe, categoria, pagador, perfil, concepto, plantilla)
        where make_date(extract(year from m.mes)::int, extract(month from m.mes)::int, x.dia) <= current_date
    loop
        v_gasto := gen_random_uuid();
        insert into public.gasto (id, hogar_id, fecha, importe, categoria_id, pagado_por, perfil_reparto_id, concepto, gasto_recurrente_id)
        values (v_gasto, v_hogar, make_date(extract(year from g.mes)::int, extract(month from g.mes)::int, g.dia),
                g.importe, g.categoria, g.pagador, g.perfil, g.concepto, g.plantilla);

        -- Mismo reparto que Core.Domain: el último adulto (Marcos) absorbe el céntimo sobrante.
        if g.perfil = p_indiv then
            insert into public.gasto_reparto (gasto_id, miembro_id, hogar_id, importe_asumido)
            values (v_gasto, g.pagador, v_hogar, g.importe);
        else
            v_ana_imp := round(g.importe * (case when g.perfil = p_partes then 3 else 50 end)
                                         / (case when g.perfil = p_partes then 5 else 100 end), 2);
            insert into public.gasto_reparto (gasto_id, miembro_id, hogar_id, importe_asumido) values
                (v_gasto, v_ana,    v_hogar, v_ana_imp),
                (v_gasto, v_marcos, v_hogar, g.importe - v_ana_imp);
        end if;
    end loop;

    -- ───── Cuenta común: un gasto que paga la cuenta y otro que adelantó Marcos y aún no le han reembolsado ─────
    insert into public.gasto (hogar_id, fecha, importe, categoria_id, pagado_por, perfil_reparto_id, concepto, a_cargo_cuenta_comun) values
        (v_hogar, v_anterior + 14, 210.00, c_varios, null,     p_cuenta, 'Seguro del hogar',        true),
        (v_hogar, v_anterior + 19,  95.00, c_varios, v_marcos, p_cuenta, 'Reparación de la lavadora', true);
    insert into public.aportacion_cuenta (hogar_id, miembro_id, desde, importe) values
        (v_hogar, v_ana,    v_anterior, 250.00),
        (v_hogar, v_marcos, v_anterior, 250.00);
end
$$;

-- La función borra un usuario de auth: solo el propietario (y pg_cron, que corre como tal) puede llamarla, no los clientes por RPC.
revoke all on function public.restablecer_demo() from public, anon, authenticated;

select public.restablecer_demo();
