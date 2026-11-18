using System.Globalization;
using MiParte.Contracts;
using MiParte.Web.Reparto;

namespace MiParte.Web.Modelos;

/// <summary>
/// Borrador leído de la foto de un ticket, editable antes de confirmar: el modelo de visión puede equivocarse, así que la
/// persona puede corregir importe, concepto y fecha. Categoría y perfil no se editan (son los predeterminados del hogar y el
/// servidor los comprueba al confirmar). No guarda nada ni depende de Blazor; la vista solo enlaza y pinta.
/// </summary>
public sealed class ModeloBorradorTicket
{
    /// <summary>Crea el modelo con los datos leídos del ticket.</summary>
    /// <param name="lectura">Respuesta de <c>POST /api/chat/ticket</c>.</param>
    public ModeloBorradorTicket(LecturaTicketResponse lectura)
    {
        Original = lectura.Borrador;
        Importe = lectura.Borrador.Importe.ToString("0.00", CultureInfo.GetCultureInfo("es-ES"));
        Concepto = lectura.Borrador.Concepto;
        Fecha = lectura.Borrador.Fecha;
        var avisos = new List<string>();
        if (!lectura.ComercioLeido) avisos.Add("No he leído el comercio: escribe el concepto.");
        if (!lectura.FechaLeida) avisos.Add("No he leído la fecha: he puesto la de hoy, cámbiala si no es esa.");
        Avisos = avisos;
    }

    /// <summary>Borrador tal como lo devolvió el servidor (categoría y perfil con sus ids).</summary>
    public BorradorGastoDto Original { get; }

    /// <summary>Importe escrito (coma o punto decimal).</summary>
    public string Importe { get; set; }

    /// <summary>Concepto del gasto.</summary>
    public string Concepto { get; set; }

    /// <summary>Fecha del gasto.</summary>
    public DateOnly Fecha { get; set; }

    /// <summary>Datos que no se leyeron del ticket y conviene revisar.</summary>
    public IReadOnlyList<string> Avisos { get; }

    /// <summary>
    /// Valida lo editado y prepara la confirmación. Las mismas reglas las vuelve a aplicar el servidor; esto evita el viaje.
    /// </summary>
    /// <param name="pagador">Miembro adulto que paga.</param>
    /// <param name="peticion">Petición lista para <c>confirmar-gasto</c>, o null si hay error.</param>
    /// <returns>Mensaje de error para la persona, o null si todo es válido.</returns>
    public string? Preparar(Guid pagador, out ConfirmarGastoRequest? peticion)
    {
        peticion = null;
        if (!EntradaImporte.TryParse(Importe, out var importe) || importe > ModeloAltaGastoChat.MaxImporte)
            return EntradaImporte.MensajeInvalido;
        var concepto = Concepto?.Trim() ?? "";
        if (concepto.Length == 0) return "Escribe el concepto del gasto.";
        if (concepto.Length > ModeloAltaGastoChat.MaxConcepto)
            return $"El concepto no puede pasar de {ModeloAltaGastoChat.MaxConcepto} caracteres.";

        var b = Original;
        peticion = new ConfirmarGastoRequest(importe, concepto, Fecha, pagador, b.CategoriaId, b.PerfilRepartoId, b.Categoria, b.Perfil);
        return null;
    }
}
