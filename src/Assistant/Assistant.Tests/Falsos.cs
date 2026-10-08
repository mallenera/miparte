using System.Text.Json;
using Microsoft.Extensions.Options;
using MiParte.Assistant.Api;
using MiParte.Assistant.Api.Hogar;
using MiParte.Assistant.Api.Modelo;
using MiParte.Contracts;

namespace MiParte.Assistant.Tests;

/// <summary>Cliente de modelo guionizado: devuelve las respuestas en orden y guarda las peticiones recibidas.</summary>
internal sealed class ModeloFalso(params RespuestaModelo[] guion) : IClienteModelo
{
    private readonly Queue<RespuestaModelo> _guion = new(guion);

    public List<SolicitudModelo> Solicitudes { get; } = [];

    public Task<RespuestaModelo> CrearAsync(SolicitudModelo solicitud, CancellationToken ct)
    {
        Solicitudes.Add(solicitud);
        if (_guion.Count == 0) throw new InvalidOperationException("El guion del modelo falso se ha agotado.");
        return Task.FromResult(_guion.Dequeue());
    }

    public static RespuestaModelo Texto(string texto)
        => new([new BloqueTexto(texto)], MotivoParada.FinDeTurno);

    public static RespuestaModelo Llama(string herramienta, string argumentosJson, string id = "toolu_1")
        => new([new BloqueUsoHerramienta(id, herramienta, JsonDocument.Parse(argumentosJson).RootElement.Clone())],
            MotivoParada.UsoDeHerramienta);
}

/// <summary>Datos de un hogar de ejemplo en memoria, sin Core.Api.</summary>
internal sealed class DatosFalsos : IDatosHogar
{
    public static readonly Guid Ana = Guid.Parse("b0000000-0000-4000-8000-0000000000aa");
    public static readonly Guid Luis = Guid.Parse("b0000000-0000-4000-8000-0000000000dd");
    public static readonly Guid Alimentacion = Guid.Parse("d0000000-0000-4000-8000-000000000002");
    public static readonly Guid Super = Guid.Parse("d0000000-0000-4000-8000-000000000009");
    public static readonly Guid Ocio = Guid.Parse("d0000000-0000-4000-8000-000000000003");

    public List<string> Consultas { get; } = [];

    public Exception? Fallo { get; set; }

    public Dictionary<string, List<GastoResponse>> GastosPorMes { get; } = [];

    public Dictionary<string, ResumenMensualResponse> Resumenes { get; } = [];

    public LiquidacionResponse? Liquidacion { get; set; }

    public string NombreCategoria { get; set; } = "Alimentación";

    public Task<IReadOnlyList<MiembroDto>> MiembrosAsync(CancellationToken ct)
    {
        Consultas.Add("miembros");
        if (Fallo is not null) throw Fallo;
        IReadOnlyList<MiembroDto> l =
        [
            new(Ana, "Ana", "adulto", null, true, "admin", true, true),
            new(Luis, "Luis", "adulto", null, true, "miembro", true),
        ];
        return Task.FromResult(l);
    }

    public Task<IReadOnlyList<CategoriaDto>> CategoriasAsync(CancellationToken ct)
    {
        Consultas.Add("categorias");
        IReadOnlyList<CategoriaDto> l =
        [
            new(Alimentacion, NombreCategoria, null, null),
            new(Super, "Supermercado", Alimentacion, null),
            new(Ocio, "Ocio", null, null),
        ];
        return Task.FromResult(l);
    }

    public Task<IReadOnlyList<GastoResponse>> GastosAsync(string mes, CancellationToken ct)
    {
        Consultas.Add("gastos:" + mes);
        IReadOnlyList<GastoResponse> l = GastosPorMes.GetValueOrDefault(mes) ?? [];
        return Task.FromResult(l);
    }

    public Task<ResumenMensualResponse> ResumenAsync(string mes, CancellationToken ct)
    {
        Consultas.Add("resumen:" + mes);
        return Task.FromResult(Resumenes.GetValueOrDefault(mes) ?? new ResumenMensualResponse(mes, 0m, [], []));
    }

    public Task<LiquidacionResponse> LiquidacionAsync(string mes, CancellationToken ct)
    {
        Consultas.Add("liquidacion:" + mes);
        return Task.FromResult(Liquidacion ?? new LiquidacionResponse(mes, [], [], []));
    }

    public static GastoResponse Gasto(decimal importe, Guid categoria, Guid pagador, string? concepto = null)
        => new(Guid.NewGuid(), new DateOnly(2026, 6, 10), importe, categoria, pagador, Guid.NewGuid(), concepto, null,
            [new RepartoGastoDto(Ana, importe / 2), new RepartoGastoDto(Luis, importe / 2)]);
}

internal sealed class FabricaFalsa(IDatosHogar datos) : IFabricaDatosHogar
{
    public string? Jwt { get; private set; }

    public Guid? Hogar { get; private set; }

    public IDatosHogar Crear(string jwt, Guid hogarId)
    {
        Jwt = jwt;
        Hogar = hogarId;
        return datos;
    }
}

internal static class Ayuda
{
    public static IOptions<OpcionesAsistente> Opciones(Action<OpcionesAsistente>? ajustar = null)
    {
        var o = new OpcionesAsistente();
        ajustar?.Invoke(o);
        return Options.Create(o);
    }

    /// <summary>Reloj fijo (15 de julio de 2026) para no depender de la fecha real.</summary>
    public static TimeProvider Reloj() => new RelojFijo(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
