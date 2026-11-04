using LoyOrder.Infrastructure.Loyverse;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Uso (con la cuenta de PRUEBA):
//   dotnet user-secrets set "Loyverse:Token" "<tu token>" --project tests/integration/LoyOrder.Infrastructure.Loyverse.Demo
//   dotnet run --project tests/integration/LoyOrder.Infrastructure.Loyverse.Demo
// El token nunca se escribe en archivos del repositorio.

var config = new ConfigurationBuilder()
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection()
    .AddLogging(l => l.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning))
    .AddLoyverse(config);
await using var sp = services.BuildServiceProvider();
var loyverse = sp.GetRequiredService<ILoyverseClient>();

try
{
    var tiendas = await loyverse.ObtenerTiendasAsync();
    Console.WriteLine($"Tiendas: {string.Join(", ", tiendas.Select(t => t.Nombre))}");
    var tienda = tiendas.FirstOrDefault(t => t.Nombre == "LoyOrder") ?? tiendas.First();

    var categorias = (await loyverse.ObtenerCategoriasAsync()).Where(c => c.EliminadaEn is null).ToList();
    Console.WriteLine($"Categorías ({categorias.Count}): {string.Join(", ", categorias.Select(c => c.Nombre))}");

    var articulos = await loyverse.ObtenerArticulosAsync();
    var variantes = articulos.SelectMany(a => a.Variantes.Select(v => (a, v))).ToList();
    var noDisponibles = variantes.Where(x => x.v.EnTienda(tienda.Id) is { DisponibleParaVenta: false }).ToList();
    Console.WriteLine($"Artículos: {articulos.Count} | Variantes: {variantes.Count} | No disponibles: {string.Join(", ", noDisponibles.Select(x => x.a.Nombre))}");

    var inventario = await loyverse.ObtenerExistenciasAsync(tienda.Id);
    var sinExistencias = inventario.Where(i => i.Existencias <= 0)
        .Select(i => variantes.FirstOrDefault(x => x.v.Id == i.VarianteId))
        .Where(x => x.a is not null).Select(x => $"{x.a.Nombre} {x.v.ValorOpcion1}".Trim());
    Console.WriteLine($"Variantes con inventario: {inventario.Count} | Sin existencias: {string.Join(", ", sinExistencias)}");

    var pagos = (await loyverse.ObtenerTiposPagoAsync()).Where(p => p.EliminadoEn is null);
    Console.WriteLine($"Tipos de pago: {string.Join(", ", pagos.Select(p => $"{p.Nombre} ({p.Tipo})"))}");

    var hace1h = DateTimeOffset.UtcNow.AddHours(-1);
    Console.WriteLine($"Modificados en la última hora: {(await loyverse.ObtenerArticulosAsync(hace1h)).Count}");

    Console.WriteLine($"Peticiones usadas en la ventana de 300 s: {sp.GetRequiredService<LimitadorPeticiones>().EnVentana}");
}
catch (LoyverseNoConfiguradoException)
{
    Console.WriteLine("Falta el token. Configúralo con: dotnet user-secrets set \"Loyverse:Token\" \"<token>\" --project tests/integration/LoyOrder.Infrastructure.Loyverse.Demo");
    return 1;
}
catch (LoyverseNoAutorizadoException)
{
    Console.WriteLine("Loyverse rechazó el token (401). Revisa que sea el de la cuenta de prueba y que no esté revocado.");
    return 1;
}
catch (LoyverseException e)
{
    Console.WriteLine($"Error de Loyverse: {e.Message}");
    return 1;
}
return 0;
