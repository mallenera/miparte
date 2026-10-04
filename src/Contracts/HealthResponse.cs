namespace MiParte.Contracts;

/// <summary>Respuesta de GET /health.</summary>
/// <param name="Service">Nombre del servicio que responde.</param>
/// <param name="Status">Estado del servicio.</param>
public record HealthResponse(string Service, string Status);
