using LoyOrder.Application;
using LoyOrder.Infrastructure.Persistence;
using Npgsql;

// Prueba de integración de la sincronización (7.2.3, CU-10) con PostgreSQL real y un Loyverse falso.
// Necesita una cadena de conexión de un usuario que pueda crear bases (por ejemplo postgres), en la variable
// LOYORDER_PRUEBAS_ADMIN. Ejemplo en PowerShell:
//   $env:LOYORDER_PRUEBAS_ADMIN = "Host=localhost;Username=postgres;Password=<tu contraseña>"
//   dotnet run --project tests/integration/LoyOrder.Persistence.Tests
// Crea la base loyorder_pruebas_sync, corre los scripts del repo y al final la borra. No toca la base loyorder.
var admin = Environment.GetEnvironmentVariable("LOYORDER_PRUEBAS_ADMIN");
if (string.IsNullOrWhiteSpace(admin))
{
    Console.WriteLine("Se omite: define LOYORDER_PRUEBAS_ADMIN con la conexión de un usuario que pueda crear bases.");
    return 0;
}
const string Base = "loyorder_pruebas_sync";
var raiz = BuscarRaiz();
var total = 0;

await Admin($"DROP DATABASE IF EXISTS {Base} WITH (FORCE)");
await Admin($"CREATE DATABASE {Base}");
var cadena = new NpgsqlConnectionStringBuilder(admin) { Database = Base }.ConnectionString;
await using var fuente = NpgsqlDataSource.Create(cadena);
try
{
    // LoyOrderObjetos_y_Permisos.sql da CONNECT sobre la base "loyorder" por nombre; aquí se aplica a la base temporal
    // para que la prueba no dependa de que exista una base loyorder en la máquina.
    foreach (var script in new[] { "LoyOrderEsquema.sql", "LoyOrderObjetos_y_Permisos.sql", "LoyOrderAutenticacionCorreo.sql", "LoyOrderSincronizacion.sql", "LoyOrderCheckout.sql", "LoyOrderNotificaciones.sql", "LoyOrderVentas.sql", "LoyOrderEdicionMenu.sql", "LoyOrderEdicionPedidos.sql" })
        await Sql(File.ReadAllText(Path.Combine(raiz, "database", "scripts", script))
            .Replace("ON DATABASE loyorder TO", $"ON DATABASE {Base} TO"));
    await Sql("INSERT INTO restaurante (nombre, horario_apertura, horario_cierre, costo_envio) VALUES ('Prueba', '09:00', '22:00', 25)");

    var repo = new CatalogoRepository(fuente);
    var origen = new Origen();
    var reloj = new Reloj();
    var servicio = new SincronizarCatalogo(origen, repo, reloj);

    // 1. Primera sincronización: vincula la tienda y trae todo
    var r1 = await servicio.EjecutarAsync(null, default);
    Igual("t1", await Escalar<string>("SELECT id_tienda_loyverse FROM restaurante"), "tienda vinculada");
    Igual(3L, await Escalar<long>("SELECT count(*) FROM categoria"), "categorías (2 + Sin categoría)");
    Igual(3L, await Escalar<long>("SELECT count(*) FROM producto"), "productos");
    Igual(4L, await Escalar<long>("SELECT count(*) FROM variante_producto"), "variantes");
    Igual(3, r1.ProductosNuevos, "productos nuevos");
    Igual("Con salsa BBQ\n6 piezas", await Escalar<string>("SELECT descripcion FROM producto WHERE id_loyverse = 'a-alitas'"), "descripción sin HTML");
    Igual("Sin categoría", await Escalar<string>("SELECT c.nombre FROM producto p JOIN categoria c USING (id_categoria) WHERE p.id_loyverse = 'a-pay'"), "categoría del pay");
    Igual(0m, await Escalar<decimal>("SELECT existencias FROM variante_producto WHERE id_loyverse = 'v-limon'"), "existencias lima-limón");
    Igual(30m, await Escalar<decimal>("SELECT existencias FROM variante_producto WHERE id_loyverse = 'v-6'"), "existencias alitas");
    Igual(0L, await Escalar<long>("SELECT count(*) FROM variante_producto WHERE id_loyverse = 'v-cola'"), "existencia de variante desconocida ignorada");
    Igual(2L, await Escalar<long>("SELECT count(*) FROM sincronizacion WHERE estado = 'Correcta' AND resumen IS NOT NULL"), "registros correctos con resumen");
    Igual(3, r1.CategoriasSinEstacion, "categorías sin estación");
    Ok("primera sincronización completa");

    // 2. Segunda sincronización: cambios de precio, variante quitada y artículo eliminado
    reloj.Ahora = reloj.Ahora.AddMinutes(2);
    origen.Articulos = [
        new("a-alitas", "Alitas BBQ", "Con salsa BBQ", "c-alitas", null, false, false, [new("v-6", "6 piezas", 99m, true)]),
        new("a-limon", "Refresco lima-limón", null, "c-bebidas", null, true, true, [new("v-limon", null, 25m, true)]),
    ];
    origen.Existencias = [new("v-6", 28m), new("v-limon", 0m), new("v-cola", 47m)];
    var r2 = await servicio.EjecutarAsync(null, default);
    Igual(reloj.Ahora.AddMinutes(-2), origen.UltimoDesde, "incremental desde la sincronización anterior");
    Igual(99m, await Escalar<decimal>("SELECT precio FROM variante_producto WHERE id_loyverse = 'v-6'"), "precio actualizado");
    Igual(false, await Escalar<bool>("SELECT disponible FROM variante_producto WHERE id_loyverse = 'v-10'"), "variante quitada");
    Igual(false, await Escalar<bool>("SELECT disponible FROM producto WHERE id_loyverse = 'a-limon'"), "producto eliminado");
    Igual(false, await Escalar<bool>("SELECT disponible FROM variante_producto WHERE id_loyverse = 'v-limon'"), "variantes del eliminado");
    Igual(3L, await Escalar<long>("SELECT count(*) FROM categoria"), "sin categorías duplicadas");
    Igual(3L, await Escalar<long>("SELECT count(*) FROM producto"), "sin productos duplicados");
    Igual((0, 1, 1, 1), (r2.ProductosNuevos, r2.ProductosActualizados, r2.ProductosEliminados, r2.ExistenciasActualizadas), "conteos (nuevos, actualizados, eliminados, existencias)");
    Ok("segunda sincronización incremental");

    // 3. Historial del panel
    var historial = await repo.HistorialAsync(1, 10, default);
    Igual(4, historial.Count, "registros en el historial");
    Verdadero(historial[0].FechaInicio >= historial[^1].FechaInicio && historial.All(h => h.Estado == "Correcta"), "orden y estado");
    Ok("historial");

    // 4. Una falla de Loyverse queda registrada y no guarda nada
    origen.Fallar = true;
    try { await servicio.EjecutarAsync(null, default); throw new Exception("No lanzó"); } catch (OrigenNoDisponibleException) { }
    Igual("No fue posible conectar con Loyverse", await Escalar<string>("SELECT detalle_error FROM sincronizacion ORDER BY id_sincronizacion DESC LIMIT 1"), "falla registrada");
    Ok("falla registrada");

    // 5. Permisos del usuario de la API (privilegios mínimos, sin DELETE en el catálogo)
    foreach (var tabla in new[] { "categoria", "producto", "variante_producto", "sincronizacion" })
    {
        Igual(true, await Escalar<bool>($"SELECT has_table_privilege('loyorder_api', 'public.{tabla}', 'INSERT, UPDATE')"), $"INSERT/UPDATE en {tabla}");
        Igual(false, await Escalar<bool>($"SELECT has_table_privilege('loyorder_api', 'public.{tabla}', 'DELETE')"), $"sin DELETE en {tabla}");
    }
    Ok("permisos de loyorder_api");

    // 6. Catálogo del cliente (7.2.4) sobre lo que dejó la sincronización más casos armados con SQL
    await Sql("""
        INSERT INTO categoria (id_restaurante, id_loyverse, nombre) VALUES (1, 'c-hamb', 'Hamburguesas');
        INSERT INTO categoria (id_restaurante, id_loyverse, nombre, activo) VALUES (1, 'c-vieja', 'Temporada', false);
        INSERT INTO producto (id_categoria, id_loyverse, nombre, disponible) SELECT id_categoria, 'a-haw', 'Hawaiana', false FROM categoria WHERE id_loyverse = 'c-hamb';
        INSERT INTO producto (id_categoria, id_loyverse, nombre) SELECT id_categoria, 'a-clasica', 'Clásica' FROM categoria WHERE id_loyverse = 'c-hamb';
        INSERT INTO producto (id_categoria, id_loyverse, nombre) SELECT id_categoria, 'a-ponche', 'Ponche' FROM categoria WHERE id_loyverse = 'c-vieja';
        INSERT INTO producto (id_categoria, id_loyverse, nombre) SELECT id_categoria, 'a-refresco', 'Refresco' FROM categoria WHERE id_loyverse = 'c-bebidas';
        INSERT INTO variante_producto (id_producto, id_loyverse, nombre, precio, controla_inventario, existencias)
        SELECT p.id_producto, v.id, v.nombre, v.precio, v.controla, v.existencias
        FROM (VALUES ('a-haw', 'v-haw', NULL, 120, false, NULL), ('a-clasica', 'v-sencilla', 'Sencilla', 110, false, NULL),
                     ('a-clasica', 'v-doble', 'Doble', 0, false, NULL), ('a-ponche', 'v-ponche', NULL, 30, false, NULL),
                     ('a-refresco', 'v-cola2', 'Cola', 25, true, 48), ('a-refresco', 'v-limon2', 'Lima-limón', 22, true, 0))
             AS v(prod, id, nombre, precio, controla, existencias)
        JOIN producto p ON p.id_loyverse = v.prod;
        INSERT INTO restaurante (nombre, horario_apertura, horario_cierre, costo_envio) VALUES ('Otro', '09:00', '22:00', 25);
        INSERT INTO categoria (id_restaurante, id_loyverse, nombre) VALUES (2, 'c-otro', 'Otro restaurante');
        INSERT INTO producto (id_categoria, id_loyverse, nombre) SELECT id_categoria, 'a-otro', 'Tacos' FROM categoria WHERE id_loyverse = 'c-otro';
        INSERT INTO variante_producto (id_producto, id_loyverse, precio) SELECT id_producto, 'v-otro', 15 FROM producto WHERE id_loyverse = 'a-otro';
        """);
    var consulta = new ConsultarMenu(new MenuRepository(fuente), repo);
    var menu = await consulta.EjecutarAsync(null, default);
    Igual("Alitas|Bebidas|Hamburguesas|Sin categoría", string.Join("|", menu.Categorias.Select(c => c.Nombre)), "categorías en orden, inactiva oculta, Sin categoría al final");
    // El INSERT ... SELECT de arriba no garantiza en qué orden se asignan los id: se comparan los nombres ordenados
    // y, aparte, que las variantes salgan en el orden de su id (el orden en que la sincronización las guarda).
    Igual("Alitas BBQ:6 piezas|Refresco:Cola,Lima-limón|Clásica:Sencilla|Pay de queso:",
        string.Join("|", menu.Categorias.SelectMany(c => c.Productos).Select(p => p.Nombre + ":" + string.Join(",", p.Variantes.Select(v => v.Nombre).Order(StringComparer.Ordinal)))),
        "productos y variantes vendibles (sin hawaiana, sin precio 0, sin variantes ni productos dados de baja)");
    Verdadero(menu.Categorias.SelectMany(c => c.Productos).All(p => p.Variantes.Select(v => v.Id).SequenceEqual(p.Variantes.Select(v => v.Id).Order())),
        "variantes en el orden de su id");
    var refresco = menu.Categorias[1].Productos.Single();
    Verdadero(refresco.Variantes.Single(v => v.Nombre == "Lima-limón").Agotado && !refresco.Agotado, "lima-limón agotada, refresco disponible");
    Igual(25m, refresco.PrecioDesde, "precio desde");
    Verdadero(menu.ActualizadoEn is not null, "fecha de la última sincronización de inventario");
    Igual("Otro restaurante", (await consulta.EjecutarAsync(2, default)).Categorias.Single().Nombre, "restaurante configurado: solo su catálogo");
    try { await consulta.EjecutarAsync(99, default); throw new Exception("No lanzó"); } catch (RestauranteNoConfiguradoException) { }
    Igual(true, await Escalar<bool>("SELECT has_table_privilege('loyorder_api', 'public.variante_producto', 'SELECT')"), "SELECT para la API");
    Ok("catálogo del cliente");
    total += await CheckoutPostgres.ComprobarAsync(fuente, raiz);
    total += await VentasPostgres.ComprobarAsync(fuente);
    total += await EdicionMenuPostgres.ComprobarAsync(fuente);
    total += await TicketPostgres.ComprobarAsync(fuente);
    total += await ReportesPostgres.ComprobarAsync(fuente);
    Console.WriteLine($"\n{total} pruebas aprobadas.");
    return 0;
}
catch (Exception e)
{
    Console.WriteLine($"  FALLA: {e.Message}");
    return 1;
}
finally
{
    await fuente.DisposeAsync();
    NpgsqlConnection.ClearAllPools();
    await Admin($"DROP DATABASE IF EXISTS {Base} WITH (FORCE)");
}

async Task Admin(string sql)
{
    await using var con = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(admin) { Database = "postgres" }.ConnectionString);
    await con.OpenAsync();
    await using var cmd = new NpgsqlCommand(sql, con);
    await cmd.ExecuteNonQueryAsync();
}
async Task Sql(string sql) { await using var cmd = fuente.CreateCommand(sql); await cmd.ExecuteNonQueryAsync(); }
async Task<T> Escalar<T>(string sql) { await using var cmd = fuente.CreateCommand(sql); return (T)(await cmd.ExecuteScalarAsync())!; }
void Ok(string que) => Console.WriteLine($"  OK   {++total}. {que}");
static void Igual<T>(T esperado, T real, string que)
{
    if (!EqualityComparer<T>.Default.Equals(esperado, real)) throw new Exception($"{que}: esperaba <{esperado}>, llegó <{real}>");
}
static void Verdadero(bool c, string que) { if (!c) throw new Exception(que); }
static string BuscarRaiz()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d is not null && !File.Exists(Path.Combine(d.FullName, "LoyOrder.sln"))) d = d.Parent;
    return d?.FullName ?? throw new Exception("No encontré la raíz del repo (LoyOrder.sln).");
}

sealed class Origen : IOrigenCatalogo
{
    public bool Fallar { get; set; }
    public DateTimeOffset? UltimoDesde { get; private set; }
    public List<ArticuloOrigen> Articulos { get; set; } = [
        new("a-alitas", "Alitas BBQ", "<p>Con <b>salsa</b>&nbsp;BBQ</p><p>6 piezas</p>", "c-alitas", "https://img/alitas.png", false, false,
            [new("v-6", "6 piezas", 95m, true), new("v-10", "10 piezas", 150m, true)]),
        new("a-limon", "Refresco lima-limón", null, "c-bebidas", null, true, false, [new("v-limon", null, 25m, true)]),
        new("a-pay", "Pay de queso", null, null, null, false, false, [new("v-pay", null, 45m, true)]),
    ];
    public List<ExistenciaOrigen> Existencias { get; set; } = [new("v-limon", 0m), new("v-6", 30m), new("v-cola", 48m)];
    public Task<IReadOnlyList<TiendaOrigen>> TiendasAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<TiendaOrigen>>([new("t1", "LoyOrder")]);
    public Task<IReadOnlyList<CategoriaOrigen>> CategoriasAsync(CancellationToken ct) =>
        Fallar ? throw new OrigenNoDisponibleException() : Task.FromResult<IReadOnlyList<CategoriaOrigen>>([new("c-alitas", "Alitas", false), new("c-bebidas", "Bebidas", false)]);
    public Task<IReadOnlyList<ArticuloOrigen>> ArticulosAsync(string t, DateTimeOffset? desde, CancellationToken ct) { UltimoDesde = desde; return Task.FromResult<IReadOnlyList<ArticuloOrigen>>(Articulos); }
    public Task<IReadOnlyList<ExistenciaOrigen>> ExistenciasAsync(string t, CancellationToken ct) => Task.FromResult<IReadOnlyList<ExistenciaOrigen>>(Existencias);
}

sealed class Reloj : TimeProvider
{
    public DateTimeOffset Ahora { get; set; } = new(2026, 11, 24, 18, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Ahora;
}
