using MiParte.Contracts;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Web.Demo;

public sealed partial class ServidorDemo
{
    // Identificadores fijos para que los datos de ejemplo sean reproducibles; lo que se crea después usa Guid.NewGuid().
    private static readonly Guid IdHogar = Id(1);
    private static readonly Guid Ana = Id(10);
    private static readonly Guid Marcos = Id(11);
    private static readonly Guid Lucia = Id(12);
    private static readonly Guid PerfilCuentaComun = Id(20);
    private static readonly Guid PerfilPartes = Id(21);
    private static readonly Guid PerfilPorcentaje = Id(22);
    private static readonly Guid PerfilIndividual = Id(23);
    private static readonly Guid CatHipoteca = Id(30);
    private static readonly Guid CatAlimentacion = Id(31);
    private static readonly Guid CatSuministros = Id(32);
    private static readonly Guid CatVarios = Id(33);
    private static readonly Guid CatHijo = Id(34);
    private static readonly Guid CatOcio = Id(35);
    private static readonly Guid CatSupermercado = Id(36);
    private static readonly Guid RecHipoteca = Id(40);
    private static readonly Guid RecInternet = Id(41);

    private static Guid Id(int n) => new(n, 0, 0, new byte[8]);

    /// <summary>Hogar de ejemplo: dos adultos, una hija a cargo, los cuatro perfiles de siempre y dos meses de gastos.</summary>
    private void Sembrar()
    {
        _miembros.AddRange(
        [
            new MiembroDemo { Id = Ana, Nombre = "Ana", Rol = "admin", Vinculado = true, EsYo = true },
            new MiembroDemo { Id = Marcos, Nombre = "Marcos" },
            new MiembroDemo { Id = Lucia, Nombre = "Lucía", Tipo = TiposMiembro.ACargo, ResponsableId = Ana },
        ]);

        _perfiles.AddRange(
        [
            new PerfilDemo { Id = PerfilCuentaComun, Nombre = "Cuenta común", Modo = ModoReparto.CuentaComun },
            new PerfilDemo
            {
                Id = PerfilPartes, Nombre = "Por partes", Modo = ModoReparto.Partes,
                Detalle = [new PerfilDetalleDto(Ana, 3), new PerfilDetalleDto(Marcos, 2)],
            },
            new PerfilDemo
            {
                Id = PerfilPorcentaje, Nombre = "Porcentaje fijo", Modo = ModoReparto.Porcentaje,
                Detalle = [new PerfilDetalleDto(Ana, 50), new PerfilDetalleDto(Marcos, 50)],
            },
            new PerfilDemo { Id = PerfilIndividual, Nombre = "Individual", Modo = ModoReparto.Individual },
        ]);

        _categorias.AddRange(
        [
            new CategoriaDto(CatHipoteca, "Hipoteca/Alquiler", null, PerfilPartes),
            new CategoriaDto(CatAlimentacion, "Alimentación", null, PerfilPartes),
            new CategoriaDto(CatSuministros, "Suministros", null, PerfilPartes),
            new CategoriaDto(CatVarios, "Gastos varios de casa", null, PerfilPartes),
            new CategoriaDto(CatHijo, "Hijo", null, PerfilPartes),
            new CategoriaDto(CatOcio, "Ocio personal", null, PerfilIndividual),
            new CategoriaDto(CatSupermercado, "Supermercado", CatAlimentacion, PerfilPartes),
        ]);

        _recurrentes.AddRange(
        [
            new RecurrenteDemo
            {
                Id = RecHipoteca, Importe = 780m, CategoriaId = CatHipoteca, PagadoPor = Ana, PerfilRepartoId = PerfilPartes,
                DiaMes = 1, Concepto = "Hipoteca", Activo = true,
            },
            new RecurrenteDemo
            {
                Id = RecInternet, Importe = 39.90m, CategoriaId = CatSuministros, PagadoPor = Marcos, PerfilRepartoId = PerfilPartes,
                DiaMes = 8, Concepto = "Internet y móvil", Activo = true,
            },
        ]);

        var actual = new DateOnly(_hoy.Year, _hoy.Month, 1);
        var anterior = actual.AddMonths(-1);

        // Mes anterior completo y mes actual hasta hoy.
        foreach (var (mes, v) in new[] { (anterior, 0), (actual, 1) })
        {
            Gasto(mes, 1, 780m, CatHipoteca, Ana, PerfilPartes, "Hipoteca", RecHipoteca);
            Gasto(mes, 3, v == 0 ? 86.43m : 79.80m, CatSupermercado, Marcos, PerfilPartes, "Compra semanal");
            Gasto(mes, 6, v == 0 ? 62.15m : 58.40m, CatSuministros, Ana, PerfilPartes, "Luz");
            Gasto(mes, 8, 39.90m, CatSuministros, Marcos, PerfilPartes, "Internet y móvil", RecInternet);
            Gasto(mes, 10, v == 0 ? 28m : 35m, CatOcio, v == 0 ? Marcos : Ana, PerfilIndividual, v == 0 ? "Cine" : "Cena con amigos");
            Gasto(mes, 12, v == 0 ? 74.20m : 102.35m, CatSupermercado, Ana, PerfilPartes, "Supermercado");
            Gasto(mes, 15, 45.60m, CatVarios, Ana, PerfilPorcentaje, "Droguería");
            Gasto(mes, 18, 120m, CatHijo, Ana, PerfilPartes, "Material escolar");
            Gasto(mes, 22, 91.05m, CatSupermercado, Marcos, PerfilPartes, "Compra del mes");
        }

        // Cuenta común: un gasto que paga la propia cuenta y otro que adelantó Marcos y aún no le han reembolsado.
        Gasto(anterior, 15, 210m, CatVarios, null, PerfilCuentaComun, "Seguro del hogar");
        Gasto(anterior, 20, 95m, CatVarios, Marcos, PerfilCuentaComun, "Reparación de la lavadora");
        _aportaciones.Add(new AportacionCuentaDto(Guid.NewGuid(), Ana, anterior, 250m));
        _aportaciones.Add(new AportacionCuentaDto(Guid.NewGuid(), Marcos, anterior, 250m));
    }

    /// <summary>Añade un gasto de ejemplo con su reparto; se omite si cae en el futuro.</summary>
    private void Gasto(DateOnly mes, int dia, decimal importe, Guid categoria, Guid? pagador, Guid perfil, string concepto, Guid? recurrente = null)
    {
        var fecha = new DateOnly(mes.Year, mes.Month, dia);
        if (fecha > _hoy) return;
        var p = _perfiles.First(x => x.Id == perfil);
        Repartir(p, importe, pagador ?? Guid.Empty, out var repartos);
        _gastos.Add(new GastoDemo
        {
            Id = Guid.NewGuid(), Fecha = fecha, Importe = importe, CategoriaId = categoria, PagadoPor = pagador, PerfilRepartoId = perfil,
            Concepto = concepto, GastoRecurrenteId = recurrente, Repartos = repartos, ACargoCuentaComun = p.Modo == ModoReparto.CuentaComun,
        });
    }
}
