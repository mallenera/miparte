using MiParte.Core.Application.Gastos;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.Recurrentes;

/// <summary>Datos de una plantilla de gasto recurrente.</summary>
/// <param name="Importe">Importe mensual.</param>
/// <param name="CategoriaId">Categoría del hogar.</param>
/// <param name="PagadoPor">Adulto activo que adelanta el pago.</param>
/// <param name="PerfilRepartoId">Perfil con el que se reparte.</param>
/// <param name="DiaMes">Día del mes (1 a 28) en que se genera.</param>
/// <param name="Concepto">Descripción opcional.</param>
/// <param name="Activo">Si la plantilla genera gastos.</param>
/// <param name="Personal">Si los gastos que genera son personales (solo para quien paga; fuera de la liquidación).</param>
public sealed record DatosRecurrente(
    decimal Importe, Guid CategoriaId, Guid PagadoPor, Guid PerfilRepartoId, short DiaMes, string? Concepto, bool Activo,
    bool Personal = false);

/// <summary>Resultado de generar el mes: cuántos gastos se crearon y cuántas plantillas ya tenían el suyo.</summary>
public sealed record GeneracionDelMes(DateOnly Mes, int Creados, int YaExistentes);

/// <summary>Casos de uso de los gastos recurrentes y de la generación mensual de sus gastos.</summary>
public sealed class ServicioRecurrentes(IRepositorioRecurrentes recurrentes, IRepositorioGastos gastos)
{
    /// <summary>Veces que se reintenta una generación que choca con otra petición simultánea.</summary>
    private const int MaxReintentos = 3;

    /// <summary>Plantillas ordenadas por día del mes.</summary>
    public Task<IReadOnlyList<GastoRecurrente>> ListarAsync(CancellationToken ct) => recurrentes.ListarAsync(ct);

    /// <summary>Una plantilla; null si no existe en el hogar.</summary>
    public Task<GastoRecurrente?> ObtenerAsync(Guid id, CancellationToken ct) => recurrentes.ObtenerAsync(id, ct);

    /// <summary>Activa o desactiva los gastos recurrentes del hogar. Desactivarlos no borra las plantillas: quedan pausadas hasta volver a activarlos.</summary>
    public async Task<bool> ActivarAsync(bool activas, CancellationToken ct)
    {
        await recurrentes.EstablecerRecurrentesActivasAsync(activas, ct);
        return activas;
    }

    /// <summary>
    /// Crea una plantilla. Conflicto si el hogar tiene desactivados los gastos recurrentes; inválida si el importe, el día, la categoría,
    /// el pagador o el perfil no valen.
    /// </summary>
    public async Task<Resultado<GastoRecurrente>> CrearAsync(Guid hogarId, DatosRecurrente datos, CancellationToken ct, Guid? quien = null)
    {
        if (!await recurrentes.RecurrentesActivasAsync(ct)) return Resultado<GastoRecurrente>.Conflicto(Mensajes.RecurrentesInactivas);
        if (AjenaPersonal(datos, quien)) return Resultado<GastoRecurrente>.Invalido(ServicioGastos.PersonalDeOtro);
        if (await ValidarAsync(datos, ct) is { } error) return Resultado<GastoRecurrente>.Invalido(error);

        var plantilla = new GastoRecurrente
        {
            Id = Guid.NewGuid(), HogarId = hogarId, Importe = datos.Importe, CategoriaId = datos.CategoriaId,
            PagadoPor = datos.PagadoPor, PerfilRepartoId = datos.PerfilRepartoId, DiaMes = datos.DiaMes,
            Concepto = ReglasMovimiento.NormalizarConcepto(datos.Concepto), Activo = datos.Activo, EsPersonal = datos.Personal,
        };
        recurrentes.Anadir(plantilla);
        await recurrentes.GuardarAsync(ct);
        return Resultado<GastoRecurrente>.Ok(plantilla);
    }

    /// <summary>Actualiza la plantilla sin modificar los gastos ya generados (el historial es inmutable).</summary>
    public async Task<Resultado<GastoRecurrente>> EditarAsync(Guid id, DatosRecurrente datos, CancellationToken ct, Guid? quien = null)
    {
        var plantilla = await recurrentes.ObtenerParaEditarAsync(id, ct);
        if (plantilla is null) return Resultado<GastoRecurrente>.NoEncontrado();
        if (AjenaPersonal(datos, quien)) return Resultado<GastoRecurrente>.Invalido(ServicioGastos.PersonalDeOtro);
        if (await ValidarAsync(datos, ct) is { } error) return Resultado<GastoRecurrente>.Invalido(error);

        plantilla.Importe = datos.Importe;
        plantilla.CategoriaId = datos.CategoriaId;
        plantilla.PagadoPor = datos.PagadoPor;
        plantilla.PerfilRepartoId = datos.PerfilRepartoId;
        plantilla.DiaMes = datos.DiaMes;
        plantilla.Concepto = ReglasMovimiento.NormalizarConcepto(datos.Concepto);
        plantilla.Activo = datos.Activo;
        plantilla.EsPersonal = datos.Personal;
        await recurrentes.GuardarAsync(ct);
        return Resultado<GastoRecurrente>.Ok(plantilla);
    }

    /// <summary>Borra la plantilla; con gastos ya generados hay que desactivarla, no borrarla.</summary>
    public async Task<Resultado<bool>> BorrarAsync(Guid id, CancellationToken ct)
    {
        var plantilla = await recurrentes.ObtenerParaEditarAsync(id, ct);
        if (plantilla is null) return Resultado<bool>.NoEncontrado();
        if (await recurrentes.TieneGastosGeneradosAsync(id, ct))
            return Resultado<bool>.Conflicto("Ya tiene gastos generados: desactívala en lugar de borrarla.");

        recurrentes.Eliminar(plantilla);
        await recurrentes.GuardarAsync(ct);
        return Resultado<bool>.Ok(true);
    }

    /// <summary>
    /// Crea los gastos del mes que empieza en <paramref name="inicio"/> para las plantillas activas que aún no tengan uno. Es
    /// idempotente: o se generan todos los pendientes o ninguno. Conflicto si el hogar tiene desactivados los gastos recurrentes, si el
    /// mes está cerrado, si una plantilla va a cargo de la cuenta común y el hogar no la ha activado, o si va a cargo del ahorro y
    /// este no está activado o no alcanza; inválido si algún gasto no se puede repartir.
    /// </summary>
    public async Task<Resultado<GeneracionDelMes>> GenerarAsync(DateOnly inicio, CancellationToken ct)
    {
        if (!await recurrentes.RecurrentesActivasAsync(ct)) return Resultado<GeneracionDelMes>.Conflicto(Mensajes.RecurrentesInactivas);
        if (await gastos.MesCerradoAsync(inicio, ct)) return Resultado<GeneracionDelMes>.Conflicto(Mensajes.MesCerrado);

        // Dos peticiones simultáneas pueden leer lo mismo y generar duplicados: el índice único gasto_recurrente_mes_uq lo impide
        // y aquí se reintenta (hasta MaxReintentos veces) con una lectura nueva, que ya incluye lo que generó la otra petición.
        for (var intento = 0; ; intento++)
        {
            var plantillas = await recurrentes.ActivasAsync(ct);
            var yaGeneradas = await recurrentes.GeneradasEnElMesAsync(inicio, ct);
            var pendientes = plantillas.Where(p => !yaGeneradas.Contains(p.Id)).ToList();

            if (pendientes.Count > 0)
            {
                var miembros = await gastos.MiembrosActivosAsync(ct);
                var perfiles = await recurrentes.PerfilesAsync(ct);

                // Una plantilla a cargo de la cuenta común no genera gastos mientras el hogar no la tenga activada.
                if (pendientes.Any(p => !p.EsPersonal && ModosDeCuenta.EsDeCuentaComun(perfiles[p.PerfilRepartoId].Modo))
                    && !await gastos.CuentaComunActivaAsync(ct))
                    return Resultado<GeneracionDelMes>.Conflicto(Mensajes.CuentaInactiva);

                // Las del ahorro, además, necesitan el ahorro activado y que alcance para todas las del mes.
                var delAhorro = pendientes
                    .Where(p => !p.EsPersonal && perfiles[p.PerfilRepartoId].Modo == ModoReparto.CuentaComunAhorro).Sum(p => p.Importe);
                if (delAhorro > 0)
                {
                    if (!await gastos.AhorroActivoAsync(ct)) return Resultado<GeneracionDelMes>.Conflicto(Mensajes.AhorroInactivo);
                    var disponible = Math.Max(0m, AhorroCuenta.Disponible(inicio, await gastos.DatosAhorroAsync(null, ct)));
                    if (delAhorro > disponible)
                        return Resultado<GeneracionDelMes>.Conflicto(
                            $"El ahorro disponible ({disponible:0.00}) no cubre los gastos recurrentes del ahorro de este mes.",
                            new Dictionary<string, object> { ["disponible"] = disponible });
                }

                var generados = new List<Gasto>(pendientes.Count);
                foreach (var p in pendientes)
                {
                    var gasto = GeneracionRecurrente.CrearGasto(p, inicio, perfiles[p.PerfilRepartoId], miembros, out var error);
                    if (gasto is null) // nada se guarda: o se generan todos o ninguno
                        return Resultado<GeneracionDelMes>.Invalido(
                            $"No se puede repartir el gasto recurrente '{p.Concepto ?? p.Id.ToString()}': {error}");
                    generados.Add(gasto);
                }

                switch (await recurrentes.GuardarGeneradosAsync(generados, ct))
                {
                    case ResultadoGuardadoGenerados.MesCerrado:
                        return Resultado<GeneracionDelMes>.Conflicto(Mensajes.MesCerrado); // otra petición cerró el mes tras la comprobación
                    case ResultadoGuardadoGenerados.Duplicado when intento < MaxReintentos:
                        continue;
                    case ResultadoGuardadoGenerados.Duplicado:
                        return Resultado<GeneracionDelMes>.Conflicto("Otra petición está generando los mismos gastos: vuelve a intentarlo.");
                }
            }

            return Resultado<GeneracionDelMes>.Ok(new GeneracionDelMes(inicio, pendientes.Count, plantillas.Count - pendientes.Count));
        }
    }

    /// <summary>Una plantilla personal solo la crea o cambia quien paga (ver <see cref="ServicioGastos"/>); null de <paramref name="quien"/> = sin control.</summary>
    private static bool AjenaPersonal(DatosRecurrente d, Guid? quien) => d.Personal && quien is { } yo && d.PagadoPor != yo;

    /// <summary>Valida importe, concepto, día del mes, categoría, pagador adulto activo y perfil de reparto; devuelve el error o null.</summary>
    private async Task<string?> ValidarAsync(DatosRecurrente d, CancellationToken ct)
    {
        var e = ReglasMovimiento.ValidarImporte(d.Importe) ?? ReglasMovimiento.ValidarConcepto(d.Concepto);
        if (e is not null) return e;
        if (GeneracionRecurrente.ValidarDiaMes(d.DiaMes) is { } dia) return dia;
        if (!await gastos.CategoriaExisteAsync(d.CategoriaId, ct)) return "La categoría no existe en el hogar.";
        if (!await gastos.EsAdultoActivoAsync(d.PagadoPor, ct)) return "Quien paga debe ser un adulto activo del hogar.";
        if (await gastos.ObtenerPerfilAsync(d.PerfilRepartoId, ct) is null) return "El perfil de reparto no existe en el hogar.";
        return null;
    }
}
