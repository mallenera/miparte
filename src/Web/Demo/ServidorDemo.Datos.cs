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

    /// <summary>
    /// Hogar de ejemplo: dos adultos (Ana, que es quien usa la demo y administra, y Marcos), una hija a cargo, los cuatro perfiles de
    /// siempre y dos meses de gastos. Las entidades son las del dominio, no una copia para la demo.
    /// </summary>
    private static AlmacenDemo Sembrar(DateOnly hoy)
    {
        var a = new AlmacenDemo(new Hogar
        {
            Id = IdHogar, Nombre = "Casa de Ana y Marcos", CuentaComunActiva = true, AhorroActivo = true,
        });

        a.Miembros.AddRange(
        [
            new Miembro { Id = Ana, HogarId = IdHogar, Nombre = "Ana", Rol = RolMiembro.Admin, UserId = IdUsuario },
            new Miembro { Id = Marcos, HogarId = IdHogar, Nombre = "Marcos" },
            new Miembro { Id = Lucia, HogarId = IdHogar, Nombre = "Lucía", Tipo = TipoMiembro.ACargo, ResponsableId = Ana },
        ]);

        PerfilReparto Perfil(Guid id, string nombre, ModoReparto modo, params (Guid Miembro, decimal Valor)[] detalle)
        {
            var p = new PerfilReparto { Id = id, HogarId = IdHogar, Nombre = nombre, Modo = modo };
            p.Detalles.AddRange(detalle.Select(d => new PerfilRepartoDetalle
            {
                Id = Guid.NewGuid(), HogarId = IdHogar, PerfilId = id, MiembroId = d.Miembro, Valor = d.Valor,
            }));
            return p;
        }

        a.Perfiles.AddRange(
        [
            Perfil(PerfilCuentaComun, "Cuenta común", ModoReparto.CuentaComun),
            Perfil(PerfilPartes, "Por partes", ModoReparto.Partes, (Ana, 3), (Marcos, 2)),
            Perfil(PerfilPorcentaje, "Porcentaje fijo", ModoReparto.Porcentaje, (Ana, 50), (Marcos, 50)),
            Perfil(PerfilIndividual, "Individual", ModoReparto.Individual),
        ]);

        Categoria Cat(Guid id, string nombre, Guid? padre, Guid perfil)
            => new() { Id = id, HogarId = IdHogar, Nombre = nombre, CategoriaPadreId = padre, PerfilRepartoId = perfil };

        a.Categorias.AddRange(
        [
            Cat(CatHipoteca, "Hipoteca/Alquiler", null, PerfilPartes),
            Cat(CatAlimentacion, "Alimentación", null, PerfilPartes),
            Cat(CatSuministros, "Suministros", null, PerfilPartes),
            Cat(CatVarios, "Gastos varios de casa", null, PerfilPartes),
            Cat(CatHijo, "Hijo", null, PerfilPartes),
            Cat(CatOcio, "Ocio personal", null, PerfilIndividual),
            Cat(CatSupermercado, "Supermercado", CatAlimentacion, PerfilPartes),
        ]);

        a.Recurrentes.AddRange(
        [
            new GastoRecurrente
            {
                Id = RecHipoteca, HogarId = IdHogar, Importe = 780m, CategoriaId = CatHipoteca, PagadoPor = Ana,
                PerfilRepartoId = PerfilPartes, DiaMes = 1, Concepto = "Hipoteca", Activo = true,
            },
            new GastoRecurrente
            {
                Id = RecInternet, HogarId = IdHogar, Importe = 39.90m, CategoriaId = CatSuministros, PagadoPor = Marcos,
                PerfilRepartoId = PerfilPartes, DiaMes = 8, Concepto = "Internet y móvil", Activo = true,
            },
        ]);

        var actual = new DateOnly(hoy.Year, hoy.Month, 1);
        var anterior = actual.AddMonths(-1);

        void Gasto(DateOnly mes, int dia, decimal importe, Guid categoria, Guid? pagador, Guid perfil, string concepto, Guid? recurrente = null)
            => a.SembrarGasto(IdHogar, hoy, mes, dia, importe, categoria, pagador, perfil, concepto, recurrente);

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
        a.Aportaciones.Add(new AportacionCuenta { Id = Guid.NewGuid(), HogarId = IdHogar, MiembroId = Ana, Desde = anterior, Importe = 250m });
        a.Aportaciones.Add(new AportacionCuenta { Id = Guid.NewGuid(), HogarId = IdHogar, MiembroId = Marcos, Desde = anterior, Importe = 250m });
        return a;
    }
}
