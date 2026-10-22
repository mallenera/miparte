using System.Net;
using MiParte.Contracts;
using MiParte.Core.Application.Contacto;
using MiParte.Web.Api;

namespace MiParte.Web.Modelos;

/// <summary>Situación del formulario de contacto.</summary>
public enum EstadoContacto
{
    /// <summary>La persona está escribiendo el mensaje.</summary>
    Editando,

    /// <summary>Petición en curso.</summary>
    Enviando,

    /// <summary>Mensaje entregado.</summary>
    Enviado,
}

/// <summary>
/// Estado del formulario de contacto: datos, validación previa con las mismas reglas que la API (<see cref="ServicioContacto"/>) y
/// envío. No depende de Blazor; la página solo enlaza y pinta.
/// </summary>
public sealed class ModeloContacto
{
    /// <summary>Dirección a la que se ofrece escribir si el formulario no puede enviar.</summary>
    public const string CorreoDirecto = "mipartetuparte@gmail.com";

    /// <summary>Motivos que ofrece el formulario, con su clave para la API.</summary>
    public static readonly IReadOnlyList<(string Clave, string Texto)> Motivos =
    [
        ("duda", "Tengo una duda"),
        ("mejora", "Propongo una mejora"),
        ("otro", "Otro asunto"),
    ];

    private readonly CoreApiClient _api;

    /// <summary>Crea el modelo.</summary>
    /// <param name="api">Cliente de Core.Api.</param>
    /// <param name="email">Correo con el que rellenar el formulario (el de la sesión), si lo hay.</param>
    public ModeloContacto(CoreApiClient api, string? email = null)
    {
        _api = api;
        Email = email ?? "";
    }

    /// <summary>Clave del motivo elegido.</summary>
    public string Motivo { get; set; } = "duda";

    /// <summary>Nombre de quien escribe; opcional.</summary>
    public string Nombre { get; set; } = "";

    /// <summary>Correo para contestar.</summary>
    public string Email { get; set; }

    /// <summary>Texto del mensaje.</summary>
    public string Mensaje { get; set; } = "";

    /// <summary>Campo trampa oculto: una persona no lo ve y lo deja vacío.</summary>
    public string Web { get; set; } = "";

    /// <summary>Situación del formulario.</summary>
    public EstadoContacto Estado { get; private set; } = EstadoContacto.Editando;

    /// <summary>Error que mostrar; null si no hay.</summary>
    public string? Error { get; private set; }

    /// <summary>Indica si conviene ofrecer escribir directamente por correo (el envío no está disponible o ha fallado).</summary>
    public bool OfrecerCorreo { get; private set; }

    /// <summary>Enlace <c>mailto:</c> con el asunto del motivo elegido, para cuando el formulario no puede enviar.</summary>
    public string EnlaceCorreo =>
        $"mailto:{CorreoDirecto}?subject={Uri.EscapeDataString(ServicioContacto.Asuntos.GetValueOrDefault(Motivo, ServicioContacto.Asuntos["otro"]))}";

    /// <summary>Valida y envía el mensaje. Sin errores pasa a <see cref="EstadoContacto.Enviado"/>; si no, deja el texto escrito y el error.</summary>
    public async Task EnviarAsync()
    {
        if (Estado != EstadoContacto.Editando) return;
        Error = Validar();
        OfrecerCorreo = false;
        if (Error is not null) return;

        Estado = EstadoContacto.Enviando;
        try
        {
            await _api.EnviarContactoAsync(new ContactoRequest(Motivo, Email.Trim(), Mensaje.Trim(),
                string.IsNullOrWhiteSpace(Nombre) ? null : Nombre.Trim(), string.IsNullOrEmpty(Web) ? null : Web));
            Estado = EstadoContacto.Enviado;
        }
        catch (ApiException e)
        {
            Estado = EstadoContacto.Editando;
            (Error, OfrecerCorreo) = e.Codigo switch
            {
                HttpStatusCode.BadRequest => (e.Message, false),
                HttpStatusCode.TooManyRequests => ("Has enviado varios mensajes seguidos. Espera un rato antes de volver a escribir.", false),
                HttpStatusCode.Conflict => (e.Message, true), // modo demo
                _ => ("No se ha podido enviar el mensaje ahora mismo.", true),
            };
        }
    }

    /// <summary>Vacía el formulario para escribir otro mensaje; conserva el correo y el nombre.</summary>
    public void Nuevo()
    {
        Mensaje = "";
        Error = null;
        OfrecerCorreo = false;
        Estado = EstadoContacto.Editando;
    }

    /// <summary>Comprueba lo mismo que la API para avisar sin esperar al servidor; null si todo está bien.</summary>
    private string? Validar()
    {
        if (!ServicioContacto.EsEmailValido(Email.Trim())) return "Escribe un correo válido para poder contestarte.";
        var mensaje = Mensaje.Trim();
        if (mensaje.Length < ServicioContacto.MinLongitudMensaje)
            return $"El mensaje debe tener al menos {ServicioContacto.MinLongitudMensaje} caracteres.";
        if (mensaje.Length > ServicioContacto.MaxLongitudMensaje)
            return $"El mensaje no puede superar los {ServicioContacto.MaxLongitudMensaje} caracteres.";
        return null;
    }
}
