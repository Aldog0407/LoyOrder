using System.Reflection;
using LoyOrder.Application;

// Pruebas de SincronizarCatalogo (7.2.3, CU-10) con un Loyverse falso y un repositorio en memoria.
var pruebas = typeof(Pruebas).GetMethods(BindingFlags.Public | BindingFlags.Static)
    .Where(m => m.Name.StartsWith("Prueba_")).OrderBy(m => m.MetadataToken).ToList();
var fallidas = 0;
foreach (var p in pruebas)
{
    try { await (Task)p.Invoke(null, null)!; Console.WriteLine($"  OK   {p.Name}"); }
    catch (Exception e)
    {
        fallidas++;
        var real = e is TargetInvocationException t ? t.InnerException! : e;
        Console.WriteLine($"  FALLA {p.Name}: {real.GetType().Name}: {real.Message}");
    }
}
Console.WriteLine($"\n{pruebas.Count - fallidas} de {pruebas.Count} pruebas aprobadas.");
return fallidas == 0 ? 0 : 1;

// ------------------------------------------------------------------ dobles de prueba

sealed class OrigenFalso : IOrigenCatalogo
{
    public List<TiendaOrigen> Tiendas { get; } = [new("t1", "LoyOrder")];
    public List<CategoriaOrigen> Categorias { get; } = [];
    public List<ArticuloOrigen> Articulos { get; } = [];
    public List<ExistenciaOrigen> Existencias { get; } = [];
    public Exception? FallarEnArticulos { get; set; }
    public Exception? FallarEnExistencias { get; set; }
    public List<DateTimeOffset?> DesdePedidos { get; } = [];
    public List<string> TiendasUsadas { get; } = [];
    public TaskCompletionSource? Pausa { get; set; }

    public Task<IReadOnlyList<TiendaOrigen>> TiendasAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<TiendaOrigen>>(Tiendas);
    public async Task<IReadOnlyList<CategoriaOrigen>> CategoriasAsync(CancellationToken ct)
    {
        if (Pausa is not null) await Pausa.Task;
        return Categorias;
    }
    public Task<IReadOnlyList<ArticuloOrigen>> ArticulosAsync(string idTienda, DateTimeOffset? desde, CancellationToken ct)
    {
        TiendasUsadas.Add(idTienda); DesdePedidos.Add(desde);
        if (FallarEnArticulos is not null) throw FallarEnArticulos;
        return Task.FromResult<IReadOnlyList<ArticuloOrigen>>(Articulos);
    }
    public Task<IReadOnlyList<ExistenciaOrigen>> ExistenciasAsync(string idTienda, CancellationToken ct)
    {
        if (FallarEnExistencias is not null) throw FallarEnExistencias;
        return Task.FromResult<IReadOnlyList<ExistenciaOrigen>>(Existencias);
    }
}

sealed class RepositorioFalso : ICatalogoRepository
{
    public RestauranteSincronizable? Restaurante { get; set; } = new(7, "t1", 2);
    public List<(long Id, string Tipo, DateTimeOffset Inicio, string Estado, string? Error, string? Resumen)> Registros { get; } = [];
    public List<(IReadOnlyList<CategoriaGuardar> Categorias, IReadOnlyList<ProductoGuardar> Productos)> Guardados { get; } = [];
    public List<IReadOnlyList<ExistenciaOrigen>> ExistenciasGuardadas { get; } = [];
    public string? TiendaVinculada { get; private set; }
    public int SinEstacion { get; set; }

    public Task<RestauranteSincronizable?> RestauranteAsync(long? id, CancellationToken ct) => Task.FromResult(Restaurante);
    public Task VincularTiendaAsync(long id, string tienda, CancellationToken ct) { TiendaVinculada = tienda; return Task.CompletedTask; }
    public Task<DateTimeOffset?> UltimaCorrectaAsync(long id, string tipo, CancellationToken ct) =>
        Task.FromResult(Registros.Where(r => r.Tipo == tipo && r.Estado == TiposSincronizacion.Correcta)
            .Select(r => (DateTimeOffset?)r.Inicio).DefaultIfEmpty(null).Max());
    public Task<long> IniciarRegistroAsync(long id, string tipo, DateTimeOffset inicio, CancellationToken ct)
    {
        Registros.Add((Registros.Count + 1, tipo, inicio, TiposSincronizacion.EnProceso, null, null));
        return Task.FromResult((long)Registros.Count);
    }
    public Task TerminarRegistroAsync(long id, string estado, DateTimeOffset fin, string? error, string? resumen, CancellationToken ct)
    {
        var i = (int)id - 1; Registros[i] = Registros[i] with { Estado = estado, Error = error, Resumen = resumen };
        return Task.CompletedTask;
    }
    public Task<ResultadoCatalogo> GuardarCatalogoAsync(long id, IReadOnlyList<CategoriaGuardar> c, IReadOnlyList<ProductoGuardar> p, CancellationToken ct)
    {
        Guardados.Add((c, p));
        return Task.FromResult(new ResultadoCatalogo(c.Count, 0, p.Count(x => !x.Eliminado), 0, p.Count(x => x.Eliminado), p.Sum(x => x.Variantes.Count), 0));
    }
    public Task<int> GuardarExistenciasAsync(IReadOnlyList<ExistenciaOrigen> e, CancellationToken ct) { ExistenciasGuardadas.Add(e); return Task.FromResult(e.Count); }
    public Task<int> CategoriasSinEstacionAsync(long id, CancellationToken ct) => Task.FromResult(SinEstacion);
    public Task<IReadOnlyList<RegistroSincronizacion>> HistorialAsync(long id, int n, CancellationToken ct) => Task.FromResult<IReadOnlyList<RegistroSincronizacion>>([]);
}

sealed class RelojFalso(DateTimeOffset inicio) : TimeProvider
{
    public DateTimeOffset Ahora { get; set; } = inicio;
    public override DateTimeOffset GetUtcNow() => Ahora;
}

static class Datos
{
    public static (OrigenFalso Origen, RepositorioFalso Repo, RelojFalso Reloj, SincronizarCatalogo Servicio) Crear()
    {
        var origen = new OrigenFalso();
        origen.Categorias.AddRange([new("c-alitas", "Alitas", false), new("c-bebidas", "Bebidas", false)]);
        origen.Articulos.AddRange([
            new("a-alitas", "Alitas BBQ", "<p>Con <b>salsa</b>&nbsp;BBQ</p><p>6 piezas</p>", "c-alitas", "https://img/alitas.png", false, false,
                [new("v-6", "6 piezas", 95m, true), new("v-10", "10 piezas", 150m, true)]),
            new("a-limon", "Refresco lima-limón", null, "c-bebidas", null, true, false, [new("v-limon", null, 25m, true)]),
        ]);
        origen.Existencias.AddRange([new("v-limon", 0m)]);
        var repo = new RepositorioFalso();
        var reloj = new RelojFalso(new DateTimeOffset(2026, 11, 24, 18, 0, 0, TimeSpan.Zero));
        return (origen, repo, reloj, new SincronizarCatalogo(origen, repo, reloj));
    }
    public static void Igual<T>(T esperado, T real, string que)
    {
        if (!EqualityComparer<T>.Default.Equals(esperado, real)) throw new Exception($"{que}: esperaba {esperado}, llegó {real}");
    }
    public static void Cierto(bool condicion, string que) { if (!condicion) throw new Exception(que); }
    public static async Task<T> Lanza<T>(Func<Task> accion) where T : Exception
    {
        try { await accion(); }
        catch (T e) { return e; }
        throw new Exception($"Se esperaba {typeof(T).Name}");
    }
}

// ------------------------------------------------------------------ pruebas

static partial class Pruebas
{
    public static async Task Prueba_PrimeraSincronizacionTraeTodoElCatalogo()
    {
        var (origen, repo, _, s) = Datos.Crear();
        var r = await s.EjecutarAsync(null, default);
        Datos.Igual<DateTimeOffset?>(null, origen.DesdePedidos.Single(), "desde (FA 2.a: sin filtro de fecha)");
        Datos.Cierto(r.Completa, "debe marcarse como completa");
        Datos.Igual(2, r.ProductosNuevos, "productos");
        Datos.Igual(3, r.VariantesNuevas, "variantes");
        Datos.Igual(1, r.ExistenciasActualizadas, "existencias");
    }

    public static async Task Prueba_RegistraCatalogoEInventarioComoCorrectos()
    {
        var (_, repo, _, s) = Datos.Crear();
        await s.EjecutarAsync(null, default);
        Datos.Igual(2, repo.Registros.Count, "registros");
        Datos.Igual(TiposSincronizacion.Catalogo, repo.Registros[0].Tipo, "tipo 1");
        Datos.Igual(TiposSincronizacion.Inventario, repo.Registros[1].Tipo, "tipo 2");
        Datos.Cierto(repo.Registros.All(r => r.Estado == TiposSincronizacion.Correcta), "ambos correctos");
        Datos.Cierto(repo.Registros[0].Resumen!.Contains("\"productosNuevos\":2"), "resumen con conteos: " + repo.Registros[0].Resumen);
    }

    public static async Task Prueba_LaSegundaSincronizacionSoloPideLoModificado()
    {
        var (origen, _, reloj, s) = Datos.Crear();
        var primera = reloj.Ahora;
        await s.EjecutarAsync(null, default);
        reloj.Ahora = primera.AddMinutes(2);
        var r = await s.EjecutarAsync(null, default);
        Datos.Igual<DateTimeOffset?>(primera, origen.DesdePedidos[1], "desde = inicio de la última correcta");
        Datos.Cierto(!r.Completa, "debe ser incremental");
    }

    public static async Task Prueba_SiPasaron24HorasVuelveATraerTodo()
    {
        var (origen, _, reloj, s) = Datos.Crear();
        await s.EjecutarAsync(null, default);
        reloj.Ahora = reloj.Ahora.AddHours(25);
        await s.EjecutarAsync(null, default);
        Datos.Igual<DateTimeOffset?>(null, origen.DesdePedidos[1], "desde");
    }

    public static async Task Prueba_LimpiaElHtmlDeLaDescripcion()
    {
        var (_, repo, _, s) = Datos.Crear();
        await s.EjecutarAsync(null, default);
        var alitas = repo.Guardados[0].Productos.Single(p => p.IdLoyverse == "a-alitas");
        Datos.Igual("Con salsa BBQ\n6 piezas", alitas.Descripcion, "descripción");
    }

    public static async Task Prueba_ProductoSinCategoriaVaASinCategoria()
    {
        var (origen, repo, _, s) = Datos.Crear();
        origen.Articulos.Add(new("a-pay", "Pay de queso", null, null, null, false, false, [new("v-pay", null, 45m, true)]));
        await s.EjecutarAsync(null, default);
        var (cats, prods) = repo.Guardados[0];
        var id = SincronizarCatalogo.PrefijoSinCategoria + "7";
        Datos.Igual(id, prods.Single(p => p.IdLoyverse == "a-pay").IdCategoriaLoyverse, "categoría del pay");
        Datos.Cierto(cats.Any(c => c.IdLoyverse == id && c.Nombre == "Sin categoría" && c.Activo), "se crea Sin categoría");
    }

    public static async Task Prueba_NoCreaSinCategoriaSiNoHaceFalta()
    {
        var (_, repo, _, s) = Datos.Crear();
        await s.EjecutarAsync(null, default);
        Datos.Igual(2, repo.Guardados[0].Categorias.Count, "categorías");
    }

    public static async Task Prueba_PrecioVariableONoDisponibleEnLaTiendaNoSeVende()
    {
        var (origen, repo, _, s) = Datos.Crear();
        origen.Articulos.Add(new("a-hawaiana", "Hamburguesa hawaiana", null, "c-alitas", null, false, false,
            [new("v-haw", null, 120m, false), new("v-var", "Al peso", null, true)]));
        await s.EjecutarAsync(null, default);
        var p = repo.Guardados[0].Productos.Single(x => x.IdLoyverse == "a-hawaiana");
        Datos.Cierto(p.Variantes.All(v => !v.Disponible), "ninguna variante se puede pedir");
        Datos.Cierto(!p.Disponible, "el producto queda no disponible");
        Datos.Igual(0m, p.Variantes.Single(v => v.IdLoyverse == "v-var").Precio, "precio variable se guarda en 0");
    }

    public static async Task Prueba_ArticuloEliminadoSeMarcaComoEliminado()
    {
        var (origen, repo, _, s) = Datos.Crear();
        origen.Articulos.Add(new("a-viejo", "Producto eliminado", null, "c-bebidas", null, false, true, [new("v-viejo", null, 10m, true)]));
        var r = await s.EjecutarAsync(null, default);
        var p = repo.Guardados[0].Productos.Single(x => x.IdLoyverse == "a-viejo");
        Datos.Cierto(p.Eliminado && !p.Disponible && !p.Variantes[0].Disponible, "eliminado y no disponible");
        Datos.Igual(1, r.ProductosEliminados, "eliminados");
    }

    public static async Task Prueba_CategoriaEliminadaQuedaInactiva()
    {
        var (origen, repo, _, s) = Datos.Crear();
        origen.Categorias.Add(new("c-vieja", "Temporada", true));
        await s.EjecutarAsync(null, default);
        Datos.Cierto(repo.Guardados[0].Categorias.Single(c => c.IdLoyverse == "c-vieja") is { Activo: false }, "inactiva");
    }

    public static async Task Prueba_ControlDeInventarioPasaALasVariantes()
    {
        var (_, repo, _, s) = Datos.Crear();
        await s.EjecutarAsync(null, default);
        var limon = repo.Guardados[0].Productos.Single(x => x.IdLoyverse == "a-limon");
        Datos.Cierto(limon.Variantes.Single().ControlaInventario, "controla inventario");
        Datos.Cierto(limon.Variantes.Single().Disponible, "sin existencias sigue disponible en Loyverse: el agotado lo decide el catálogo");
    }

    public static async Task Prueba_VinculaLaTiendaSiLaCuentaTieneSoloUna()
    {
        var (origen, repo, _, s) = Datos.Crear();
        repo.Restaurante = new(7, null, 2);
        await s.EjecutarAsync(null, default);
        Datos.Igual("t1", repo.TiendaVinculada, "tienda vinculada");
        Datos.Igual("t1", origen.TiendasUsadas.Single(), "tienda usada");
    }

    public static async Task Prueba_ConVariasTiendasPideConfigurarla()
    {
        var (origen, repo, _, s) = Datos.Crear();
        repo.Restaurante = new(7, null, 2);
        origen.Tiendas.Add(new("t2", "Sucursal"));
        await Datos.Lanza<RestauranteNoConfiguradoException>(() => s.EjecutarAsync(null, default));
        Datos.Igual(0, repo.Registros.Count, "no se registra nada");
    }

    public static async Task Prueba_SinRestauranteNoSincroniza()
    {
        var (_, repo, _, s) = Datos.Crear();
        repo.Restaurante = null;
        await Datos.Lanza<RestauranteNoConfiguradoException>(() => s.EjecutarAsync(null, default));
    }

    public static async Task Prueba_TokenRechazadoRegistraFallaYNoGuarda()
    {
        var (origen, repo, _, s) = Datos.Crear();
        origen.FallarEnArticulos = new OrigenNoAutorizadoException();
        await Datos.Lanza<OrigenNoAutorizadoException>(() => s.EjecutarAsync(null, default));
        Datos.Igual(0, repo.Guardados.Count, "no guarda nada (EX-02)");
        Datos.Igual(TiposSincronizacion.Fallida, repo.Registros.Single().Estado, "estado");
        Datos.Igual("El token de Loyverse no es válido", repo.Registros.Single().Error, "mensaje (EX-01)");
    }

    public static async Task Prueba_FallaDeExistenciasNoDeshaceElCatalogo()
    {
        var (origen, repo, _, s) = Datos.Crear();
        origen.FallarEnExistencias = new OrigenNoDisponibleException();
        await Datos.Lanza<OrigenNoDisponibleException>(() => s.EjecutarAsync(null, default));
        Datos.Igual(TiposSincronizacion.Correcta, repo.Registros[0].Estado, "catálogo");
        Datos.Igual(TiposSincronizacion.Fallida, repo.Registros[1].Estado, "inventario");
        Datos.Igual("No fue posible conectar con Loyverse", repo.Registros[1].Error, "mensaje");
    }

    public static async Task Prueba_NoPermiteDosSincronizacionesALaVez()
    {
        var (origen, _, _, s) = Datos.Crear();
        origen.Pausa = new TaskCompletionSource();
        var primera = s.EjecutarAsync(null, default);
        await Datos.Lanza<SincronizacionEnCursoException>(() => s.EjecutarAsync(null, default));
        origen.Pausa.SetResult();
        await primera;
    }

    public static async Task Prueba_AvisaDeCategoriasSinEstacion()
    {
        var (_, repo, _, s) = Datos.Crear();
        repo.SinEstacion = 2;
        var r = await s.EjecutarAsync(null, default);
        Datos.Igual(2, r.CategoriasSinEstacion, "conteo");
        Datos.Igual("Hay categorías nuevas sin estación asignada", r.Aviso, "aviso (FA 4.a)");
    }

    public static Task Prueba_LimpiadorHtmlRespetaTextoNormal()
    {
        Datos.Igual("Hamburguesa doble", LimpiadorHtml.Limpiar("  Hamburguesa   doble "), "texto normal");
        Datos.Igual<string?>(null, LimpiadorHtml.Limpiar("<p> </p>"), "solo etiquetas");
        Datos.Igual("Papas & aderezo", LimpiadorHtml.Limpiar("Papas &amp; aderezo"), "entidades");
        return Task.CompletedTask;
    }
}
