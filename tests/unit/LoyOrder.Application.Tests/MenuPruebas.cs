using LoyOrder.Application;

// Pruebas de ConsultarMenu (7.2.4, catálogo por categorías) con repositorios en memoria.
sealed class MenuFalso : IMenuRepository
{
    public List<FilaMenu> Filas { get; } = [];
    public List<long> Consultados { get; } = [];
    public Task<IReadOnlyList<FilaMenu>> ListarAsync(long idRestaurante, CancellationToken ct)
    {
        Consultados.Add(idRestaurante);
        return Task.FromResult<IReadOnlyList<FilaMenu>>(Filas);
    }
}

static class DatosMenu
{
    public static FilaMenu Fila(long cat, string categoria, long prod, string producto, long variante, string? nombre,
        decimal precio, bool controla = false, decimal? existencias = null) =>
        new(cat, categoria, prod, producto, null, null, variante, nombre, precio, controla, existencias);

    public static (MenuFalso Menu, RepositorioFalso Catalogo, ConsultarMenu Servicio) Crear()
    {
        var menu = new MenuFalso();
        menu.Filas.AddRange([
            Fila(1, "Alitas", 10, "Alitas BBQ", 100, "6 piezas", 95m, true, 30m),
            Fila(1, "Alitas", 10, "Alitas BBQ", 101, "10 piezas", 150m, true, 30m),
            Fila(2, "Bebidas", 20, "Refresco", 200, "Cola", 25m, true, 48m),
            Fila(2, "Bebidas", 20, "Refresco", 201, "Lima-limón", 22m, true, 0m),
            Fila(2, "Bebidas", 21, "Agua natural", 210, null, 18m),
        ]);
        var catalogo = new RepositorioFalso { Restaurante = new(9001, "t1", 2) };
        return (menu, catalogo, new ConsultarMenu(menu, catalogo));
    }
}

static partial class Pruebas
{
    public static async Task Prueba_MenuAgrupaPorCategoriaYProductoEnOrden()
    {
        var (_, _, s) = DatosMenu.Crear();
        var m = await s.EjecutarAsync(null, default);
        Datos.Igual("Alitas,Bebidas", string.Join(",", m.Categorias.Select(c => c.Nombre)), "categorías");
        Datos.Igual("Refresco,Agua natural", string.Join(",", m.Categorias[1].Productos.Select(p => p.Nombre)), "productos en el orden del repositorio");
        Datos.Igual(2, m.Categorias[0].Productos.Single().Variantes.Count, "variantes de las alitas");
    }

    public static async Task Prueba_MenuUsaElRestauranteConfiguradoOElPrimero()
    {
        var (menu, catalogo, s) = DatosMenu.Crear();
        await s.EjecutarAsync(null, default);
        Datos.Igual(9001L, menu.Consultados.Single(), "sin configuración: el restaurante que resuelve el repositorio");
        catalogo.Restaurante = null;
        await Datos.Lanza<RestauranteNoConfiguradoException>(() => s.EjecutarAsync(5, default));
        Datos.Igual(1, menu.Consultados.Count, "sin restaurante no consulta el menú");
    }

    public static async Task Prueba_MenuMarcaAgotadaLaVarianteSinExistencias()
    {
        var (_, _, s) = DatosMenu.Crear();
        var refresco = (await s.EjecutarAsync(null, default)).Categorias[1].Productos[0];
        Datos.Cierto(refresco.Variantes.Single(v => v.Nombre == "Lima-limón").Agotado, "lima-limón agotada");
        Datos.Cierto(!refresco.Variantes.Single(v => v.Nombre == "Cola").Agotado, "cola disponible");
        Datos.Cierto(!refresco.Agotado, "el producto sigue disponible");
        Datos.Igual(25m, refresco.PrecioDesde, "precio desde: solo variantes disponibles");
    }

    public static async Task Prueba_MenuProductoAgotadoSiTodasSusVariantesLoEstan()
    {
        var (menu, _, s) = DatosMenu.Crear();
        menu.Filas.Clear();
        menu.Filas.AddRange([
            DatosMenu.Fila(1, "Postres", 30, "Pay", 300, "Queso", 45m, true, 0m),
            DatosMenu.Fila(1, "Postres", 30, "Pay", 301, "Limón", 40m, true, -1m),
        ]);
        var pay = (await s.EjecutarAsync(null, default)).Categorias[0].Productos[0];
        Datos.Cierto(pay.Agotado, "pay agotado");
        Datos.Igual(40m, pay.PrecioDesde, "precio desde de un producto agotado");
    }

    public static async Task Prueba_MenuExistenciasDesconocidasNoAgotan()
    {
        var (menu, _, s) = DatosMenu.Crear();
        menu.Filas.Clear();
        menu.Filas.Add(DatosMenu.Fila(1, "Alitas", 10, "Alitas", 100, "6 piezas", 95m, true, null));
        var v = (await s.EjecutarAsync(null, default)).Categorias[0].Productos[0].Variantes[0];
        Datos.Cierto(!v.Agotado, "sin dato de existencias no se marca agotada");
    }

    public static async Task Prueba_MenuVarianteUnicaSinNombreYPrecioCeroDescartado()
    {
        var (menu, _, s) = DatosMenu.Crear();
        menu.Filas.Add(DatosMenu.Fila(3, "Extras", 40, "Precio variable", 400, null, 0m));
        var m = await s.EjecutarAsync(null, default);
        Datos.Igual<string?>(null, m.Categorias[1].Productos[1].Variantes.Single().Nombre, "variante sin nombre");
        Datos.Cierto(m.Categorias.All(c => c.Nombre != "Extras"), "precio 0 no se muestra");
    }

    public static async Task Prueba_MenuIncluyeLaUltimaSincronizacionDeInventario()
    {
        var (_, catalogo, s) = DatosMenu.Crear();
        Datos.Igual<DateTimeOffset?>(null, (await s.EjecutarAsync(null, default)).ActualizadoEn, "nunca sincronizado");
        var hora = new DateTimeOffset(2026, 11, 24, 18, 0, 0, TimeSpan.Zero);
        var id = await catalogo.IniciarRegistroAsync(9001, TiposSincronizacion.Inventario, hora, default);
        await catalogo.TerminarRegistroAsync(id, TiposSincronizacion.Correcta, hora, null, null, default);
        Datos.Igual<DateTimeOffset?>(hora, (await s.EjecutarAsync(null, default)).ActualizadoEn, "última correcta");
    }
}
