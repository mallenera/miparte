using Microsoft.Extensions.Options;

namespace MiParte.Assistant.Api.Seguridad;

/// <summary>La persona, o el asistente en conjunto, ha agotado las preguntas del día.</summary>
public sealed class CuotaAgotadaException()
    : Exception("Se ha alcanzado el límite de preguntas al asistente por hoy. Mañana vuelve a estar disponible.");

/// <summary>
/// Tope diario de preguntas al modelo, por usuario y en total. El límite por minuto frena las ráfagas, pero no el gasto sostenido:
/// el registro es gratuito y cada pregunta cuesta tokens de pago, así que sin un tope diario cualquiera podría agotar el saldo.
/// Vive en memoria (singleton): se reinicia con el proceso y cada instancia lleva su cuenta, lo que basta para acotar el coste; el
/// límite de gasto de la consola del proveedor sigue siendo la última barrera.
/// </summary>
/// <param name="opciones">Topes configurados (<c>PreguntasPorDia</c> y <c>PreguntasPorDiaGlobal</c>).</param>
/// <param name="reloj">Reloj; el día cambia a medianoche UTC.</param>
public sealed class CuotaDiaria(IOptions<OpcionesAsistente> opciones, TimeProvider reloj)
{
    private readonly object _cerrojo = new();
    private readonly Dictionary<string, int> _porUsuario = [];
    private DateOnly _dia;
    private int _total;

    /// <summary>Anota una pregunta del usuario si quedan cupo propio y cupo global; si no, no anota nada.</summary>
    /// <param name="usuario">Identificador del usuario autenticado (claim <c>sub</c>).</param>
    /// <returns>false si el usuario o el asistente han agotado las preguntas de hoy.</returns>
    public bool Consumir(string usuario)
    {
        var o = opciones.Value;
        lock (_cerrojo)
        {
            var hoy = DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime);
            if (hoy != _dia)
            {
                _dia = hoy;
                _total = 0;
                _porUsuario.Clear();
            }

            var usadas = _porUsuario.GetValueOrDefault(usuario);
            if (usadas >= o.PreguntasPorDia || _total >= o.PreguntasPorDiaGlobal) return false;
            _porUsuario[usuario] = usadas + 1;
            _total++;
            return true;
        }
    }
}
