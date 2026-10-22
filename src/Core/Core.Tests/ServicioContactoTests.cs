using MiParte.Core.Application;
using MiParte.Core.Application.Contacto;

namespace MiParte.Core.Tests;

/// <summary>Formulario de contacto probado sin HTTP: validación, composición del correo y trampa para bots.</summary>
public class ServicioContactoTests
{
    internal sealed class EnviadorFalso(bool disponible = true, bool acepta = true) : IEnviadorCorreo
    {
        public List<CorreoContacto> Enviados { get; } = new();

        public bool Disponible => disponible;

        public Task<bool> EnviarAsync(CorreoContacto correo, CancellationToken ct)
        {
            Enviados.Add(correo);
            return Task.FromResult(acepta);
        }
    }

    private static MensajeContacto Mensaje(string? motivo = "duda", string? email = "ana@ejemplo.es",
        string? mensaje = "¿Cómo reparto un gasto entre tres?", string? nombre = "Ana", string? trampa = null)
        => new(motivo, email, mensaje, nombre, trampa);

    [Fact]
    public async Task Enviar_ComponeElCorreoConElAsuntoDelMotivoYResponderAQuienEscribe()
    {
        var enviador = new EnviadorFalso();

        var r = await new ServicioContacto(enviador).EnviarAsync(Mensaje(motivo: "mejora", email: "  ana@ejemplo.es "), default);

        Assert.Null(r.Error);
        var correo = Assert.Single(enviador.Enviados);
        Assert.Equal("Mi parte, tu parte: propuesta de mejora", correo.Asunto);
        Assert.Equal("ana@ejemplo.es", correo.ResponderA);
        Assert.Contains("Nombre: Ana", correo.Texto);
        Assert.Contains("Correo: ana@ejemplo.es", correo.Texto);
        Assert.EndsWith("¿Cómo reparto un gasto entre tres?", correo.Texto);
    }

    [Fact]
    public async Task Enviar_SinNombre_LoIndicaYUnNombreConSaltosDeLineaQuedaEnUnaLinea()
    {
        var enviador = new EnviadorFalso();
        var servicio = new ServicioContacto(enviador);

        await servicio.EnviarAsync(Mensaje(nombre: null), default);
        await servicio.EnviarAsync(Mensaje(nombre: "Ana\r\nCorreo: otro@x.es"), default);

        Assert.Contains("Nombre: (sin nombre)", enviador.Enviados[0].Texto);
        Assert.Contains("Nombre: Ana Correo: otro@x.es", enviador.Enviados[1].Texto);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("queja")]
    [InlineData("Duda")]
    public async Task Enviar_ConMotivoDesconocido_EsInvalidoYNoEnvia(string? motivo)
    {
        var enviador = new EnviadorFalso();

        var r = await new ServicioContacto(enviador).EnviarAsync(Mensaje(motivo: motivo), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
        Assert.Empty(enviador.Enviados);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ana")]
    [InlineData("ana@ejemplo")]
    [InlineData("Ana <ana@ejemplo.es>")]
    [InlineData("ana@ejemplo.es, otro@ejemplo.es")]
    public async Task Enviar_ConCorreoNoValido_EsInvalido(string? email)
    {
        var enviador = new EnviadorFalso();

        var r = await new ServicioContacto(enviador).EnviarAsync(Mensaje(email: email), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
        Assert.Contains("correo", r.Error.Mensaje);
        Assert.Empty(enviador.Enviados);
    }

    [Fact]
    public async Task Enviar_ConMensajeDemasiadoCortoOLargo_EsInvalido()
    {
        var enviador = new EnviadorFalso();
        var servicio = new ServicioContacto(enviador);

        var corto = await servicio.EnviarAsync(Mensaje(mensaje: "  hola   "), default);
        var largo = await servicio.EnviarAsync(Mensaje(mensaje: new string('a', ServicioContacto.MaxLongitudMensaje + 1)), default);
        var justo = await servicio.EnviarAsync(Mensaje(mensaje: new string('a', ServicioContacto.MaxLongitudMensaje)), default);

        Assert.Equal(TipoError.Invalido, corto.Error!.Tipo);
        Assert.Equal(TipoError.Invalido, largo.Error!.Tipo);
        Assert.Null(justo.Error);
        Assert.Single(enviador.Enviados);
    }

    [Fact]
    public async Task Enviar_ConNombreDemasiadoLargo_EsInvalido()
    {
        var r = await new ServicioContacto(new EnviadorFalso())
            .EnviarAsync(Mensaje(nombre: new string('a', ServicioContacto.MaxLongitudNombre + 1)), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task Enviar_ConLaTrampaRellena_RespondeBienSinEnviarNada()
    {
        var enviador = new EnviadorFalso();

        // Ni siquiera se valida: el bot no debe aprender qué lo delata.
        var r = await new ServicioContacto(enviador).EnviarAsync(Mensaje(email: "no-es-correo", trampa: "https://spam.example"), default);

        Assert.Null(r.Error);
        Assert.Empty(enviador.Enviados);
    }

    [Fact]
    public async Task Enviar_SinEnvioConfigurado_NoEstaDisponible()
    {
        var enviador = new EnviadorFalso(disponible: false);

        var r = await new ServicioContacto(enviador).EnviarAsync(Mensaje(), default);

        Assert.Equal(TipoError.NoDisponible, r.Error!.Tipo);
        Assert.Empty(enviador.Enviados);
    }

    [Fact]
    public async Task Enviar_SiElProveedorLoRechaza_NoEstaDisponible()
    {
        var r = await new ServicioContacto(new EnviadorFalso(acepta: false)).EnviarAsync(Mensaje(), default);

        Assert.Equal(TipoError.NoDisponible, r.Error!.Tipo);
    }
}
