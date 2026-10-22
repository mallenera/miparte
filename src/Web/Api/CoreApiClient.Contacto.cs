using MiParte.Contracts;

namespace MiParte.Web.Api;

/// <summary>Formulario de contacto.</summary>
public sealed partial class CoreApiClient
{
    /// <summary>Envía un mensaje de contacto (<c>POST /api/contacto</c>). No necesita sesión ni hogar.</summary>
    /// <param name="peticion">Motivo, correo, mensaje y, si lo hay, el nombre.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task EnviarContactoAsync(ContactoRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarSinRespuestaAsync(HttpMethod.Post, "api/contacto", ct, peticion);
}
