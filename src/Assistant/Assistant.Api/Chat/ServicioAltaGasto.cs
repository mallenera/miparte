using System.Net;
using MiParte.Assistant.Api.Hogar;
using MiParte.Contracts;

namespace MiParte.Assistant.Api.Chat;

/// <summary>
/// Guarda un gasto propuesto por el asistente. Solo se invoca desde la confirmación explícita de la persona
/// (<c>POST /api/chat/confirmar-gasto</c>): el modelo nunca llega aquí. Usa el JWT y el hogar de la persona, así que
/// Core.Api aplica sus permisos (<c>gastos.crear</c>) y sus reglas de reparto y cierre de mes.
/// </summary>
public sealed class ServicioAltaGasto
{
    /// <summary>Guarda el gasto con la categoría y el perfil predeterminados del hogar en ese momento.</summary>
    /// <param name="peticion">Datos que confirma la persona.</param>
    /// <param name="datos">Acceso a los datos del hogar de la persona.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <exception cref="SolicitudInvalidaException">Los datos no son válidos.</exception>
    /// <exception cref="GastoRechazadoException">El hogar no tiene predeterminados o Core.Api rechazó el gasto.</exception>
    public async Task<GastoResponse> ConfirmarAsync(ConfirmarGastoRequest peticion, IDatosHogar datos, CancellationToken ct)
    {
        AltaGasto.ValidarImporte(peticion.Importe);
        var concepto = AltaGasto.ValidarConcepto(peticion.Concepto);
        if (peticion.Fecha == default) throw new SolicitudInvalidaException("Indica la fecha del gasto.");
        if (peticion.PagadoPor == Guid.Empty) throw new SolicitudInvalidaException("Indica quién paga el gasto.");

        var categoria = AltaGasto.Unico((await datos.CategoriasAsync(ct)).Where(c => c.PredeterminadoGastos).ToList())
            ?? throw new GastoRechazadoException(HttpStatusCode.Conflict, AltaGasto.SinCategoria);
        var perfil = AltaGasto.Unico((await datos.PerfilesAsync(ct)).Where(p => p.PredeterminadoGastos).ToList())
            ?? throw new GastoRechazadoException(HttpStatusCode.Conflict, AltaGasto.SinPerfil);

        return await datos.CrearGastoAsync(
            new GastoRequest(peticion.Fecha, peticion.Importe, categoria.Id, peticion.PagadoPor, perfil.Id, concepto), ct);
    }
}
