using System.Net;
using System.Text.Json;
using LoyOrder.Api;
using LoyOrder.Application;
using LoyOrder.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
var fake = new Repositorio();
await using var app = ApiHost.Crear([], services => services.AddSingleton<ICategoriaRepository>(fake));
app.Urls.Add("http://127.0.0.1:0");
await app.StartAsync();
using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
var total = 0;
async Task Verificar(string ruta, int esperado, string? contenido = null)
{
    using var respuesta = await client.GetAsync(ruta);
    var cuerpo = await respuesta.Content.ReadAsStringAsync();
    if ((int)respuesta.StatusCode != esperado || (contenido != null && !cuerpo.Contains(contenido)))
        throw new Exception($"{ruta}: {respuesta.StatusCode} {cuerpo}");
    if (esperado >= 400 && respuesta.Content.Headers.ContentType?.MediaType != "application/problem+json")
        throw new Exception("Error sin ProblemDetails");
    if (cuerpo.Contains("DETALLE_INTERNO_NO_PUBLICAR")) throw new Exception("Se filtró un detalle interno");
    if (esperado >= 400 && !cuerpo.Contains("traceId")) throw new Exception("Falta identificador de seguimiento");
    Console.WriteLine($"OK {++total}: {ruta} -> {esperado}");
}
try
{
    await Verificar("/health/live", 200);
    await Verificar("/health/ready", 200);
    await Verificar("/api/v1/categorias?idRestaurante=1", 200, "Bebidas");
    foreach(var query in new[]{"", "?idRestaurante=0", "?idRestaurante=abc", "?idRestaurante=1&pagina=0", "?idRestaurante=1&tamano=101", "?idRestaurante=1&pagina=2147483647"})
        await Verificar("/api/v1/categorias"+query,400);
    await Verificar("/no-existe",404);
    await Verificar("/api/v1/categorias?idRestaurante=2",200,"[]");
    fake.Fallo = 1;
    await Verificar("/health/ready",503);
    await Verificar("/api/v1/categorias?idRestaurante=1",503);
    fake.Fallo = 2;
    await Verificar("/api/v1/categorias?idRestaurante=1",500,"Ocurri");
    try { await new ConsultarCategorias(fake).EjecutarAsync(0,1,20,default); throw new Exception("Faltó validación de aplicación"); }
    catch(SolicitudInvalidaException) { Console.WriteLine($"OK {++total}: validación de aplicación"); }
    Console.WriteLine($"{total} pruebas aprobadas.");
}
finally { await app.StopAsync(); }
sealed class Repositorio : ICategoriaRepository
{
    public int Fallo {get;set;}
    public Task<bool> DisponibleAsync(CancellationToken ct) => Task.FromResult(Fallo == 0);
    public Task<IReadOnlyList<Categoria>> ListarAsync(long restaurante,int pagina,int tamano,CancellationToken ct)
    {
        if(Fallo == 1) throw new DependenciaNoDisponibleException();
        if(Fallo == 2) throw new InvalidOperationException("DETALLE_INTERNO_NO_PUBLICAR");
        return Task.FromResult<IReadOnlyList<Categoria>>(restaurante==1 ? [new(1,1,null,"Bebidas",true)] : []);
    }
}
