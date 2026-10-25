using MiParte.Core.Application.Notificaciones;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.Gastos;

/// <summary>Datos de un gasto que se crea o se edita.</summary>
/// <param name="Fecha">Fecha del gasto.</param>
/// <param name="Importe">Importe total.</param>
/// <param name="CategoriaId">Categoría del hogar.</param>
/// <param name="PagadoPor">Adulto activo que adelanta el pago (también si va a cargo de la cuenta común o del ahorro, que se lo deben); null si lo paga la cuenta común o el ahorro directamente.</param>
/// <param name="PerfilRepartoId">Perfil con el que se reparte.</param>
/// <param name="Concepto">Descripción opcional.</param>
/// <param name="PagadoDesdeAhorro">Si se paga con el ahorro de la cuenta común; el perfil de cuenta común (ahorro) lo implica.</param>
/// <param name="Personal">Si lo asume íntegro quien lo paga y queda fuera de la liquidación.</param>
public sealed record DatosGasto(
    DateOnly Fecha, decimal Importe, Guid CategoriaId, Guid? PagadoPor, Guid PerfilRepartoId, string? Concepto,
    bool PagadoDesdeAhorro, bool Personal);

/// <summary>Casos de uso de los gastos: cada gasto guarda su reparto calculado al crearlo o editarlo, y solo entonces.</summary>
public sealed class ServicioGastos(IRepositorioGastos repositorio, IPublicadorNotificaciones? notificaciones = null)
{
    /// <summary>Gastos que cumplen el filtro, del más reciente al más antiguo.</summary>
    public Task<IReadOnlyList<Gasto>> ListarAsync(FiltroGastos filtro, CancellationToken ct)
        => repositorio.ListarAsync(filtro, ct);

    /// <summary>Un gasto con su reparto; null si no existe en el hogar.</summary>
    public Task<Gasto?> ObtenerAsync(Guid id, CancellationToken ct) => repositorio.ObtenerAsync(id, ct);

    /// <summary>
    /// Crea el gasto y guarda su reparto en la misma transacción. Es inválido si los datos o el reparto no valen; es un
    /// conflicto si el mes está cerrado, si va a cargo de la cuenta común y el hogar no la ha activado, o si se paga desde el
    /// ahorro y este no alcanza. Tras guardarlo avisa a los demás miembros (salvo el pagador, que es quien normalmente lo anota;
    /// los gastos personales no avisan).
    /// </summary>
    public async Task<Resultado<Gasto>> CrearAsync(Guid hogarId, DatosGasto datos, CancellationToken ct, Guid? quien = null)
    {
        if (AjenoAlPersonal(datos, quien)) return Resultado<Gasto>.Invalido(PersonalDeOtro);
        var preparado = await PrepararAsync(datos, ct);
        if (preparado.Error is { } invalido) return Resultado<Gasto>.Invalido(invalido);
        if (await ComprobarAsync(datos, preparado, excluir: null, [datos.Fecha], ct) is { } fallo) return fallo;

        var gasto = new Gasto
        {
            Id = Guid.NewGuid(), HogarId = hogarId, Fecha = datos.Fecha, Importe = datos.Importe,
            CategoriaId = datos.CategoriaId, PagadoPor = datos.PagadoPor, PerfilRepartoId = datos.PerfilRepartoId,
            Concepto = ReglasMovimiento.NormalizarConcepto(datos.Concepto), ACargoCuentaComun = preparado.CuentaComun,
            PagadoDesdeAhorro = preparado.DesdeAhorro, EsPersonal = datos.Personal,
        };
        RepartoGasto.Aplicar(gasto, preparado.Partes!);
        repositorio.Anadir(gasto);
        if (!await repositorio.GuardarAsync(ct)) return Resultado<Gasto>.Conflicto(Mensajes.MesCerrado);

        if (!gasto.EsPersonal && notificaciones is not null)
            notificaciones.Publicar(await AvisoGastoNuevoAsync(gasto, datos.PagadoPor, ct));
        return Resultado<Gasto>.Ok(gasto);
    }

    /// <summary>
    /// Aviso de un gasto nuevo: de dónde sale (quién lo realiza, la cuenta común o el ahorro), importe, categoría y descripción si
    /// la hay. Un gasto de la cuenta común o del ahorro lo reciben todos los miembros; el resto no va a quien lo paga, que es quien
    /// normalmente lo anota.
    /// </summary>
    private async Task<NotificacionPush> AvisoGastoNuevoAsync(Gasto gasto, Guid? pagadoPor, CancellationToken ct)
    {
        string titulo;
        Guid? excluir = pagadoPor;
        if (gasto.PagadoDesdeAhorro)
        {
            titulo = "Nuevo gasto pagado desde el ahorro";
            excluir = null;
        }
        else if (gasto.ACargoCuentaComun)
        {
            titulo = "Nuevo gasto de la cuenta común";
            excluir = null;
        }
        else
        {
            var miembros = await repositorio.MiembrosActivosAsync(ct);
            var nombre = pagadoPor is { } pagador ? miembros.FirstOrDefault(m => m.Id == pagador)?.Nombre : null;
            titulo = nombre is null ? "Nuevo gasto" : $"Nuevo gasto realizado por {nombre}";
        }

        var categoria = await repositorio.NombreCategoriaAsync(gasto.CategoriaId, ct) ?? "Sin categoría";
        var partes = new List<string> { $"{FormatoAvisos.Importe(gasto.Importe)} €", categoria };
        if (!string.IsNullOrWhiteSpace(gasto.Concepto)) partes.Add(gasto.Concepto);
        return new NotificacionPush(gasto.HogarId, excluir, titulo, string.Join(" · ", partes), "/gastos");
    }

    /// <summary>
    /// Un gasto personal lo anota y lo cambia solo quien lo paga. <paramref name="quien"/> es el miembro de la petición; null lo usa
    /// el propio sistema (sin control, p. ej. la demo interna) y la API siempre lo informa.
    /// </summary>
    private static bool AjenoAlPersonal(DatosGasto datos, Guid? quien)
        => datos.Personal && quien is { } yo && datos.PagadoPor != yo;

    /// <summary>Mensaje cuando alguien intenta anotar o cambiar un gasto personal que no es suyo.</summary>
    public const string PersonalDeOtro = "Un gasto personal solo puede anotarlo y cambiarlo quien lo paga.";

    /// <summary>Actualiza el gasto y recalcula solo su reparto, sin tocar el resto del histórico. No encontrado si no existe o es un personal ajeno.</summary>
    public Task<Resultado<Gasto>> EditarAsync(Guid id, DatosGasto datos, CancellationToken ct, Guid? quien = null)
        => repositorio.EnExclusionAsync(async () =>
        {
            var gasto = await repositorio.ObtenerParaEditarAsync(id, ct);
            if (gasto is null) return Resultado<Gasto>.NoEncontrado();
            if (AjenoAlPersonal(datos, quien)) return Resultado<Gasto>.Invalido(PersonalDeOtro);
            // Un gasto ya liquidado por un ciclo cerrado no cambia: el cierre ya lo dio por bueno con su importe y reparto.
            if (gasto.CicloLiquidacionId is not null) return Resultado<Gasto>.Conflicto(Mensajes.GastoEnCiclo);
            var preparado = await PrepararAsync(datos, ct);
            if (preparado.Error is { } invalido) return Resultado<Gasto>.Invalido(invalido);
            // Ni sacarlo de un mes cerrado ni meterlo en uno.
            if (await ComprobarAsync(datos, preparado, excluir: id, [gasto.Fecha, datos.Fecha], ct) is { } fallo) return fallo;

            gasto.Fecha = datos.Fecha;
            gasto.Importe = datos.Importe;
            gasto.CategoriaId = datos.CategoriaId;
            gasto.PagadoPor = datos.PagadoPor;
            gasto.PerfilRepartoId = datos.PerfilRepartoId;
            gasto.Concepto = ReglasMovimiento.NormalizarConcepto(datos.Concepto);
            gasto.ACargoCuentaComun = preparado.CuentaComun;
            gasto.PagadoDesdeAhorro = preparado.DesdeAhorro;
            gasto.EsPersonal = datos.Personal;
            RepartoGasto.Aplicar(gasto, preparado.Partes!);
            return await repositorio.GuardarAsync(ct) ? Resultado<Gasto>.Ok(gasto) : Resultado<Gasto>.Conflicto(Mensajes.MesCerrado);
        }, ct);

    /// <summary>Borra el gasto y su reparto. No encontrado si no existe; conflicto si su mes está cerrado o si ya liquida un ciclo cerrado.</summary>
    public Task<Resultado<bool>> BorrarAsync(Guid id, CancellationToken ct)
        => repositorio.EnExclusionAsync(async () =>
        {
            var gasto = await repositorio.ObtenerParaEditarAsync(id, ct);
            if (gasto is null) return Resultado<bool>.NoEncontrado();
            if (gasto.CicloLiquidacionId is not null) return Resultado<bool>.Conflicto(Mensajes.GastoEnCiclo);
            if (await repositorio.MesCerradoAsync(gasto.Fecha, ct)) return Resultado<bool>.Conflicto(Mensajes.MesCerrado);
            if (await repositorio.FechaEnPeriodoCerradoAsync(gasto.Fecha, ct)) return Resultado<bool>.Conflicto(Mensajes.PeriodoCerrado);

            repositorio.Eliminar(gasto);
            return await repositorio.GuardarAsync(ct) ? Resultado<bool>.Ok(true) : Resultado<bool>.Conflicto(Mensajes.MesCerrado);
        }, ct);

    /// <summary>Resultado de validar los datos: el reparto, si va a cargo de la cuenta común y si sale del ahorro, o el mensaje de error.</summary>
    private sealed record Preparado(ResultadoReparto? Partes, string? Error, bool CuentaComun, bool DesdeAhorro = false);

    /// <summary>
    /// Valida los datos y calcula el reparto: vacío y marcado a cargo de la cuenta común si el perfil es de cuenta común (o de
    /// cuenta común (ahorro), que además lo paga desde el ahorro); el 100 % del pagador si el gasto es personal. Un gasto a cargo
    /// de la cuenta o del ahorro puede adelantarlo un adulto: queda pendiente de reembolso, como en la cuenta común.
    /// </summary>
    private async Task<Preparado> PrepararAsync(DatosGasto d, CancellationToken ct)
    {
        var e = ReglasMovimiento.ValidarImporte(d.Importe) ?? ReglasMovimiento.ValidarConcepto(d.Concepto);
        if (e is not null) return new(null, e, false);
        if (d.Fecha == default) return new(null, "La fecha es obligatoria.", false);
        if (!await repositorio.CategoriaExisteAsync(d.CategoriaId, ct)) return new(null, "La categoría no existe en el hogar.", false);
        if (d.PagadoPor is { } pagador && !await repositorio.EsAdultoActivoAsync(pagador, ct))
            return new(null, "Quien paga debe ser un adulto activo del hogar.", false);
        if (d.Personal && (d.PagadoPor is null || d.PagadoDesdeAhorro))
            return new(null, "Un gasto personal lo paga un miembro del hogar, no la cuenta común ni el ahorro.", false);
        var perfil = await repositorio.ObtenerPerfilAsync(d.PerfilRepartoId, ct);
        if (perfil is null) return new(null, "El perfil de reparto no existe en el hogar.", false);
        var deCuenta = ModosDeCuenta.EsDeCuentaComun(perfil.Modo);
        if (d.PagadoPor is null && !deCuenta)
            return new(null, "La cuenta común solo paga gastos con el perfil de cuenta común.", false);
        if (d.PagadoDesdeAhorro && !deCuenta)
            return new(null, "Un gasto pagado desde el ahorro debe usar el perfil de cuenta común (ahorro).", false);

        // Un gasto personal lo asume íntegro quien lo paga, sea cual sea el modo del perfil, y no es de la cuenta común.
        if (d.Personal) return new(new ResultadoReparto([new ParteAsumida(d.PagadoPor!.Value, d.Importe)], []), null, false);

        var miembros = await repositorio.MiembrosActivosAsync(ct);
        var partes = RepartoGasto.RepartirConDependientes(perfil, miembros, d.Importe, d.PagadoPor ?? Guid.Empty, out var error);
        return new(partes, error, deCuenta, d.PagadoDesdeAhorro || perfil.Modo == ModoReparto.CuentaComunAhorro);
    }

    /// <summary>
    /// Comprobaciones de estado, en este orden: cuenta común activada si el gasto va a su cargo, meses abiertos y ahorro
    /// suficiente si se paga desde él. Devuelve el conflicto o null si se puede seguir.
    /// </summary>
    private async Task<Resultado<Gasto>?> ComprobarAsync(
        DatosGasto d, Preparado preparado, Guid? excluir, IEnumerable<DateOnly> fechas, CancellationToken ct)
    {
        if (preparado.CuentaComun && !await repositorio.CuentaComunActivaAsync(ct))
            return Resultado<Gasto>.Conflicto(Mensajes.CuentaInactiva);
        foreach (var f in fechas.Distinct())
        {
            if (await repositorio.MesCerradoAsync(f, ct)) return Resultado<Gasto>.Conflicto(Mensajes.MesCerrado);
            if (await repositorio.FechaEnPeriodoCerradoAsync(f, ct)) return Resultado<Gasto>.Conflicto(Mensajes.PeriodoCerrado);
        }

        if (!preparado.DesdeAhorro) return null;
        if (!await repositorio.CuentaComunActivaAsync(ct)) return Resultado<Gasto>.Conflicto(Mensajes.CuentaInactiva);
        if (!await repositorio.AhorroActivoAsync(ct)) return Resultado<Gasto>.Conflicto(Mensajes.AhorroInactivo);
        var disponible = Math.Max(0m, AhorroCuenta.Disponible(d.Fecha, await repositorio.DatosAhorroAsync(excluir, ct)));
        return d.Importe <= disponible ? null
            : Resultado<Gasto>.Conflicto(
                $"El ahorro disponible ({disponible:0.00}) no cubre el gasto.",
                new Dictionary<string, object> { ["disponible"] = disponible });
    }
}
