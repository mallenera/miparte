using System.Net;
using MiParte.Assistant.Api.Herramientas;
using MiParte.Assistant.Api.Hogar;
using MiParte.Assistant.Api.Modelo;
using MiParte.Contracts;

namespace MiParte.Assistant.Api.Chat;

/// <summary>No se ha podido leer el importe total del ticket: sin él no hay borrador que proponer.</summary>
public sealed class TicketIlegibleException()
    : Exception("No he podido leer el importe total del ticket. Prueba con una foto más nítida y con el ticket entero, o escribe el gasto en el chat.");

/// <summary>
/// Convierte la foto de un ticket en un borrador de gasto, igual que <c>proponer_gasto</c>: categoría y perfil son los
/// predeterminados del hogar y nada se guarda. Lo que el modelo no lee se rellena para que la tarjeta sea editable (concepto
/// genérico, fecha de hoy) y se marca, para que el front pida revisarlo. La imagen no se guarda ni se registra.
/// </summary>
/// <param name="lector">Modelo de visión.</param>
/// <param name="reloj">Reloj, para validar la fecha leída y como fecha por defecto.</param>
public sealed class ServicioTicket(ILectorTickets lector, TimeProvider reloj)
{
    /// <summary>Concepto que se propone cuando no se lee el comercio.</summary>
    public const string ConceptoPorDefecto = "Compra";

    /// <summary>Lee el ticket y prepara el borrador.</summary>
    /// <param name="imagen">Foto validada.</param>
    /// <param name="tipoMime">Tipo MIME de la foto.</param>
    /// <param name="datos">Datos del hogar (categorías y perfiles predeterminados).</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <exception cref="TicketIlegibleException">El modelo no ha devuelto un importe válido.</exception>
    /// <exception cref="GastoRechazadoException">El hogar no tiene exactamente un predeterminado de categoría o de perfil (409).</exception>
    public async Task<LecturaTicketResponse> LeerAsync(byte[] imagen, string tipoMime, IDatosHogar datos, CancellationToken ct)
    {
        var hoy = DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime);
        var lectura = FotoTicket.Interpretar(await lector.LeerAsync(imagen, tipoMime, ct), hoy);
        if (lectura.Importe is not { } importe) throw new TicketIlegibleException();

        var categoria = AltaGasto.Unico((await datos.CategoriasAsync(ct)).Where(c => c.PredeterminadoGastos).ToList())
            ?? throw new GastoRechazadoException(HttpStatusCode.Conflict, AltaGasto.SinCategoria);
        var perfil = AltaGasto.Unico((await datos.PerfilesAsync(ct)).Where(p => p.PredeterminadoGastos).ToList())
            ?? throw new GastoRechazadoException(HttpStatusCode.Conflict, AltaGasto.SinPerfil);

        // Los nombres van limpios igual que en proponer_gasto: la confirmación los compara con los actuales.
        var borrador = new BorradorGastoDto(
            importe, lectura.Comercio ?? ConceptoPorDefecto, lectura.Fecha ?? hoy,
            CatalogoHerramientas.Limpiar(categoria.Nombre), CatalogoHerramientas.Limpiar(perfil.Nombre), categoria.Id, perfil.Id);
        return new LecturaTicketResponse(borrador, lectura.Comercio is not null, lectura.Fecha is not null);
    }
}
