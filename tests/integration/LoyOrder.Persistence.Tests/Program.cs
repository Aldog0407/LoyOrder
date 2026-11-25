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
    foreach (var script in new[] { "LoyOrderEsquema.sql", "LoyOrderObjetos_y_Permisos.sql", "LoyOrderAutenticacionCorreo.sql", "LoyOrderSincronizacion.sql" })
        await Sql(File.ReadAllText(Path.Combine(raiz, "database", "scripts", script)));
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
    Console.WriteLine($"\n{total} de 5 pruebas aprobadas.");
    return total == 5 ? 0 : 1;
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
