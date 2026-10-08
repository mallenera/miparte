using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;

namespace MiParte.Assistant.Api.Modelo;

/// <summary>
/// <see cref="IClienteModelo"/> sobre el SDK oficial de Anthropic (Messages API). La clave sale solo de la
/// variable de entorno <c>ANTHROPIC_API_KEY</c> (nunca del repo ni de <c>appsettings</c>); sin ella el cliente
/// no se crea y cada llamada falla con <see cref="ModeloNoDisponibleException"/>.
/// </summary>
public sealed class ClienteAnthropic : IClienteModelo
{
    private readonly AnthropicClient? _cliente;
    private readonly string _modelo;

    /// <summary>Crea el cliente con el modelo configurado y la clave de <c>ANTHROPIC_API_KEY</c> si existe.</summary>
    /// <param name="opciones">Opciones del asistente (modelo).</param>
    /// <param name="config">Configuración de la que se lee la clave (variables de entorno).</param>
    public ClienteAnthropic(IOptions<OpcionesAsistente> opciones, IConfiguration config)
    {
        _modelo = opciones.Value.Modelo;
        var clave = config["ANTHROPIC_API_KEY"];
        if (!string.IsNullOrWhiteSpace(clave))
            _cliente = new AnthropicClient { ApiKey = clave };
    }

    /// <inheritdoc />
    public async Task<RespuestaModelo> CrearAsync(SolicitudModelo solicitud, CancellationToken ct)
    {
        if (_cliente is null)
            throw new ModeloNoDisponibleException("Falta la variable de entorno ANTHROPIC_API_KEY.");

        var parametros = new MessageCreateParams
        {
            Model = _modelo,
            MaxTokens = solicitud.MaxTokens,
            System = solicitud.Sistema,
            Messages = solicitud.Mensajes.Select(AMensaje).ToList(),
            Tools = solicitud.Herramientas.Select(AHerramienta).ToList(),
        };

        Message respuesta;
        try
        {
            respuesta = await _cliente.Messages.Create(parametros, ct);
        }
        catch (AnthropicApiException ex)
        {
            // Solo el tipo de excepción: el mensaje puede incluir fragmentos de la petición.
            throw new ModeloNoDisponibleException($"Anthropic respondió con un error ({ex.GetType().Name}).", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ModeloNoDisponibleException("No se pudo contactar con Anthropic.", ex);
        }

        return new RespuestaModelo(
            respuesta.Content.Select(DeBloque).OfType<BloqueModelo>().ToList(),
            Motivo(respuesta),
            (int)respuesta.Usage.InputTokens,
            (int)respuesta.Usage.OutputTokens);
    }

    private static MotivoParada Motivo(Message m)
    {
        var motivo = m.StopReason?.ToString() ?? "";
        return motivo switch
        {
            _ when motivo.Contains("tool", StringComparison.OrdinalIgnoreCase) => MotivoParada.UsoDeHerramienta,
            _ when motivo.Contains("max", StringComparison.OrdinalIgnoreCase) => MotivoParada.LimiteDeTokens,
            _ when motivo.Contains("refusal", StringComparison.OrdinalIgnoreCase) => MotivoParada.Rechazo,
            _ when motivo.Contains("end", StringComparison.OrdinalIgnoreCase) => MotivoParada.FinDeTurno,
            _ => MotivoParada.Otro,
        };
    }

    private static BloqueModelo? DeBloque(ContentBlock b)
    {
        if (b.TryPickText(out TextBlock? texto)) return new BloqueTexto(texto.Text);
        if (b.TryPickThinking(out ThinkingBlock? razon)) return new BloqueRazonamiento(razon.Thinking, razon.Signature);
        if (b.TryPickRedactedThinking(out RedactedThinkingBlock? oculto)) return new BloqueRazonamientoOculto(oculto.Data);
        if (b.TryPickToolUse(out ToolUseBlock? uso))
            return new BloqueUsoHerramienta(uso.ID, uso.Name, JsonSerializer.SerializeToElement(uso.Input));
        return null;
    }

    private static MessageParam AMensaje(MensajeModelo m)
    {
        var bloques = new List<ContentBlockParam>();
        foreach (var b in m.Bloques)
        {
            switch (b)
            {
                case BloqueTexto t:
                    bloques.Add(new TextBlockParam { Text = t.Texto });
                    break;
                case BloqueRazonamiento r:
                    bloques.Add(new ThinkingBlockParam { Thinking = r.Texto, Signature = r.Firma });
                    break;
                case BloqueRazonamientoOculto o:
                    bloques.Add(new RedactedThinkingBlockParam { Data = o.Datos });
                    break;
                case BloqueUsoHerramienta u:
                    bloques.Add(new ToolUseBlockParam
                    {
                        ID = u.Id,
                        Name = u.Nombre,
                        Input = u.Entrada.Deserialize<Dictionary<string, JsonElement>>() ?? [],
                    });
                    break;
                case BloqueResultadoHerramienta r:
                    bloques.Add(new ToolResultBlockParam { ToolUseID = r.IdUso, Content = r.Contenido, IsError = r.EsError });
                    break;
            }
        }

        return new MessageParam { Role = m.Rol == RolModelo.Usuario ? Role.User : Role.Assistant, Content = bloques };
    }

    private static ToolUnion AHerramienta(DefinicionHerramienta h)
    {
        var esquema = h.EsquemaEntrada;
        var propiedades = esquema.TryGetProperty("properties", out var p)
            ? p.Deserialize<Dictionary<string, JsonElement>>() ?? []
            : [];
        var requeridas = esquema.TryGetProperty("required", out var r)
            ? r.Deserialize<List<string>>() ?? []
            : [];
        return new Tool
        {
            Name = h.Nombre,
            Description = h.Descripcion,
            InputSchema = new() { Properties = propiedades, Required = requeridas },
        };
    }
}
