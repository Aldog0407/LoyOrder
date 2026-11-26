using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using LoyOrder.Api;
using LoyOrder.Application;
using LoyOrder.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT","Development");
var users = new UsuariosPrueba();
var fake = new Repositorio();
var reloj = new RelojPrueba();
var origen = new OrigenPrueba();
var catalogo = new CatalogoPrueba();
var menu = new MenuPrueba();
await using var app = ApiHost.Crear(["--Auth:SesionMaximaMinutos=60"], services =>
{
    services.AddSingleton<ICategoriaRepository>(fake);
    services.AddSingleton<IPedidoClienteRepository>(new PedidosPrueba());
    services.AddSingleton<IHistorialPedidoRepository>(new PedidosPrueba());
    services.AddSingleton<ICrearPedidoRepository>(new CheckoutPrueba());
    services.AddSingleton<IOperacionPedidoRepository>(new OperacionPrueba());
    services.AddSingleton(TimeZoneInfo.Utc);
    services.AddSingleton<IUsuarioRepository>(users);
    services.AddSingleton<TimeProvider>(reloj);
    services.AddSingleton<IOrigenCatalogo>(origen);
    services.AddSingleton<ICatalogoRepository>(catalogo);
    services.AddSingleton<IMenuRepository>(menu);
});
// La suite combinada supera los 20 intentos por minuto del acceso normal.
// Este servidor temporal comprueba permisos y flujos sin limitar la frecuencia.
app.MapControllers().DisableRateLimiting();
app.Urls.Add("http://127.0.0.1:0");
await app.StartAsync();
using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
var total = 0;
string ultimaRenovacion = "";
async Task<string> Login(string correo="cliente@prueba.test")
{
    using var response=await client.PostAsJsonAsync("/api/v1/auth/login",new{correo,contrasena="Prueba12345!"});
    response.EnsureSuccessStatusCode();
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    ultimaRenovacion = json.RootElement.GetProperty("refreshToken").GetString()!;
    return json.RootElement.GetProperty("accessToken").GetString()!;
}
client.DefaultRequestHeaders.Authorization=new("Bearer",await Login());
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
    // 7.2.4 · catálogo por categorías: el restaurante lo resuelve la API, no se pide en la URL
    fake.Fallo = 0;
    await Verificar("/api/v1/catalogo", 200, "\"id\":2,\"nombre\":\"Lima-lim");
    await Verificar("/api/v1/catalogo", 200, "\"precio\":22,\"agotado\":true");
    await Verificar("/api/v1/catalogo?idRestaurante=2", 200, "Refresco");
    if (menu.Consultados.Distinct().Single() != 1) throw new Exception("El catálogo debe usar el restaurante resuelto por la API");
    Console.WriteLine($"OK {++total}: el catálogo ignora idRestaurante de la URL");
    menu.Fallo = 1;
    await Verificar("/api/v1/catalogo", 503);
    menu.Fallo = 2;
    await Verificar("/api/v1/catalogo", 500, "Ocurri");
    menu.Fallo = 0;
    catalogo.Restaurante = null;
    await Verificar("/api/v1/catalogo", 409, "restaurante");
    catalogo.Restaurante = new(1,"t1",2);
    await VerificarAcceso();
    Console.WriteLine($"{total} pruebas aprobadas.");
}
finally { await app.StopAsync(); }
async Task VerificarAcceso()
{
    fake.Fallo=0;
    async Task Check(HttpMethod method,string ruta,int status,object? datos=null)
    {
        using var request=new HttpRequestMessage(method,ruta);
        if(datos!=null)request.Content=JsonContent.Create(datos);
        using var response=await client.SendAsync(request);
        var body=await response.Content.ReadAsStringAsync();
        if((int)response.StatusCode!=status)throw new Exception($"{ruta}: esperaba {status}, obtuvo {response.StatusCode} {body}");
        if(body.Contains("contrasenaHash") || body.Contains(users.Hash))throw new Exception("Hash expuesto");
        Console.WriteLine($"OK {++total}: {method} {ruta} -> {status}");
    }
    client.DefaultRequestHeaders.Authorization=null;
    await Check(HttpMethod.Get,"/api/v1/auth/me",401);
    await Check(HttpMethod.Get,"/api/v1/categorias?idRestaurante=1",401);
    await Check(HttpMethod.Get,"/api/v1/catalogo",401);
    await Check(HttpMethod.Post,"/api/v1/auth/registro",201,new{nombre="Nuevo",correo="nuevo@prueba.test",contrasena="Prueba12345!",telefono="123"});
    await Check(HttpMethod.Post,"/api/v1/auth/registro",409,new{nombre="Nuevo",correo="NUEVO@prueba.test",contrasena="Prueba12345!"});
    await Check(HttpMethod.Post,"/api/v1/auth/registro",400,new{nombre="Nuevo",correo="otro@prueba.test",contrasena="Prueba12345!",rol="ADMINISTRADOR"});
    await Check(HttpMethod.Post,"/api/v1/auth/registro",400,new{nombre="Nuevo",correo="otro@prueba.test",contrasena="corta"});
    await Check(HttpMethod.Post,"/api/v1/auth/login",401,new{correo="cliente@prueba.test",contrasena="incorrecta"});
    await Check(HttpMethod.Post,"/api/v1/auth/login",401,new{correo="noexiste@prueba.test",contrasena="incorrecta"});
    await Check(HttpMethod.Post,"/api/v1/auth/login",403,new{correo="inactivo@prueba.test",contrasena="Prueba12345!"});
    await Check(HttpMethod.Post,"/api/v1/auth/registro",400,new{nombre="Sin símbolo",correo="sin-simbolo@prueba.test",contrasena="SoloLetras123"});
    await Check(HttpMethod.Post,"/api/v1/auth/registro",400,new{nombre="Espacios",correo="espacios@prueba.test",contrasena="Solo Letras123"});
    await Check(HttpMethod.Post,"/api/v1/auth/registro",201,new{nombre="Símbolo",correo="simbolo@prueba.test",contrasena="abcdefgh!"});
    foreach(var (clave,esperado) in new[]{("abcdefgh",false),("abcd1234",false),("abcd 1234",false),("abcdef!",false),("abcdefg!",true),(new string('a',127)+"!",true),(new string('a',128)+"!",false),("abcdefgh★",true),("abcdefgh😀",true)})
    {
        if(PoliticaContrasena.Cumple(clave)!=esperado)throw new Exception("Política de contraseña inconsistente");
        Console.WriteLine($"OK {++total}: política de contraseña, longitud {clave.Length}, válido {esperado}");
    }
    try
    {
        await new Acceso(users,new Contrasenas()).RegistrarAsync("Rechazo","rechazo@prueba.test","abcdefgh",null,default);
        throw new Exception("La aplicación permitió registrar sin símbolo");
    }
    catch(SolicitudInvalidaException e) when(e.Message==PoliticaContrasena.Mensaje)
    { Console.WriteLine($"OK {++total}: aplicación rechaza registro sin símbolo"); }
    var token=await Login("nuevo@prueba.test");
    client.DefaultRequestHeaders.Authorization=new("Bearer",token);
    await Check(HttpMethod.Get,"/api/v1/auth/me",200);
    await Check(HttpMethod.Get,"/api/v1/categorias?idRestaurante=1",200);
    await Check(HttpMethod.Post,"/api/v1/auth/logout",204);
    await Check(HttpMethod.Get,"/api/v1/auth/me",401);
    foreach(var correo in new[]{"admin@prueba.test","operador@prueba.test"})
    {
        client.DefaultRequestHeaders.Authorization=new("Bearer",await Login(correo));
        await Check(HttpMethod.Get,"/api/v1/auth/me",200);
        await Check(HttpMethod.Get,"/api/v1/categorias?idRestaurante=1",403);
        await Check(HttpMethod.Get,"/api/v1/catalogo",403);
    }
    // 7.2.3 · CU-10: solo el administrador puede sincronizar y ver el historial
    client.DefaultRequestHeaders.Authorization=null;
    await Check(HttpMethod.Post,"/api/v1/sincronizacion",401);
    await Check(HttpMethod.Get,"/api/v1/sincronizacion",401);
    foreach(var correo in new[]{"cliente@prueba.test","operador@prueba.test"})
    {
        client.DefaultRequestHeaders.Authorization=new("Bearer",await Login(correo));
        await Check(HttpMethod.Post,"/api/v1/sincronizacion",403);
        await Check(HttpMethod.Get,"/api/v1/sincronizacion",403);
    }
    client.DefaultRequestHeaders.Authorization=new("Bearer",await Login("admin@prueba.test"));
    await Check(HttpMethod.Post,"/api/v1/sincronizacion",200);
    await Check(HttpMethod.Get,"/api/v1/sincronizacion",200);
    origen.Falla = new OrigenNoAutorizadoException();
    await Check(HttpMethod.Post,"/api/v1/sincronizacion",502);
    origen.Falla = new OrigenNoDisponibleException();
    await Check(HttpMethod.Post,"/api/v1/sincronizacion",503);
    origen.Falla = null;
    catalogo.Restaurante = null;
    await Check(HttpMethod.Post,"/api/v1/sincronizacion",409);
    catalogo.Restaurante = new(1,"t1",2);

    client.DefaultRequestHeaders.Authorization=new("Bearer",await Login());
    client.DefaultRequestHeaders.Authorization = null;
    await Check(HttpMethod.Get,"/api/v1/pedidos/opciones",401);
    await Check(HttpMethod.Post,"/api/v1/pedidos",401,new{});
    await Check(HttpMethod.Get,"/api/v1/pedidos",401);
    foreach (var personal in new[] { "admin@prueba.test", "operador@prueba.test" })
    {
        client.DefaultRequestHeaders.Authorization = new("Bearer",await Login(personal));
        await Check(HttpMethod.Get,"/api/v1/pedidos/opciones",403);
        await Check(HttpMethod.Post,"/api/v1/pedidos",403,new{});
        await Check(HttpMethod.Get,"/api/v1/pedidos",403);
    }
    client.DefaultRequestHeaders.Authorization = new("Bearer",await Login());
    await Check(HttpMethod.Get,"/api/v1/pedidos/opciones",200);
    await Check(HttpMethod.Get,"/api/v1/pedidos",200);
    await Check(HttpMethod.Get,"/api/v1/pedidos?pagina=0",400);
    client.DefaultRequestHeaders.Authorization=null;
    await Check(HttpMethod.Get,"/api/v1/operacion/pedidos",401);
    await Check(HttpMethod.Post,"/api/v1/operacion/pedidos/PRUEBA-1/estado",401,new {estado="En preparación"});
    foreach (var cuentaOperacion in new[] { "cliente@prueba.test","admin@prueba.test" })
    {
        client.DefaultRequestHeaders.Authorization=new("Bearer",await Login(cuentaOperacion));
        await Check(HttpMethod.Get,"/api/v1/operacion/pedidos",403);
        await Check(HttpMethod.Post,"/api/v1/operacion/pedidos/PRUEBA-1/estado",403,new {estado="En preparación"});
    }
    client.DefaultRequestHeaders.Authorization=new("Bearer",await Login("operador@prueba.test"));
    await Check(HttpMethod.Get,"/api/v1/operacion/pedidos",200);
    await Check(HttpMethod.Post,"/api/v1/operacion/pedidos/PRUEBA-1/estado",200,new {estado="En preparación"});
    await Check(HttpMethod.Post,"/api/v1/operacion/pedidos/PRUEBA-1/estado",400,new {estado="Cancelado"});
    await Check(HttpMethod.Post,"/api/v1/operacion/pedidos/PRUEBA-1/estado",400,new {estado="Inventado"});
    await Check(HttpMethod.Post,"/api/v1/operacion/pedidos/PRUEBA-1/estado",400,new {estado="Entregado",idEmpleado=999});
    await Check(HttpMethod.Post,"/api/v1/operacion/pedidos/NO-EXISTE/estado",404,new {estado="En preparación"});
    client.DefaultRequestHeaders.Authorization=new("Bearer",await Login());
    var operacionCheckout = Guid.NewGuid();
    var nuevaCompra = new SolicitudPedido(operacionCheckout,"Recolección",1,200,[new(11,2,100)],null);
    await Check(HttpMethod.Post,"/api/v1/pedidos",201,nuevaCompra);
    await Check(HttpMethod.Post,"/api/v1/pedidos",201,nuevaCompra);
    await Check(HttpMethod.Post,"/api/v1/pedidos",409,nuevaCompra with { TotalEsperado=100,Articulos=[new(11,1,100)] });
    await Check(HttpMethod.Post,"/api/v1/pedidos",400,nuevaCompra with { IdOperacion=Guid.Empty });
    await Check(HttpMethod.Post,"/api/v1/pedidos",400,new { idOperacion=Guid.NewGuid(),tipoEntrega="Recolección",idMetodoPago=1,totalEsperado=200,
        articulos=new[] { new { idVariante=11,cantidad=2,precioEsperado=100 } },idCliente=999 });
    users.InactivarCliente=true;
    await Check(HttpMethod.Get,"/api/v1/auth/me",401);
    users.InactivarCliente=false;
    var valid=await Login();
    client.DefaultRequestHeaders.Authorization=new("Bearer",valid[..^10]+(valid[^10]=='a' ? "b":"a")+valid[^9..]);
    await Check(HttpMethod.Get,"/api/v1/auth/me",401);
    client.DefaultRequestHeaders.Authorization = new("Bearer", await Login());
    await Check(HttpMethod.Get, "/api/v1/pedidos/PRUEBA-1", 200);
    await Check(HttpMethod.Get, "/api/v1/pedidos/AJENO", 404);
    await Check(HttpMethod.Get, "/api/v1/pedidos/" + new string('x', 31), 400);
    await Check(HttpMethod.Post, "/api/v1/pedidos/PRUEBA-1/calificacion", 400, new { calificacion = 6 });
    await Check(HttpMethod.Post, "/api/v1/pedidos/PRUEBA-1/calificacion", 400, new { calificacion = 5, idCliente = 1 });
    await Check(HttpMethod.Post, "/api/v1/pedidos/PRUEBA-1/calificacion", 204, new { calificacion = 5, comentario = "Bien" });
    client.DefaultRequestHeaders.Authorization = new("Bearer", await Login("nuevo@prueba.test"));
    await Check(HttpMethod.Get, "/api/v1/pedidos/PRUEBA-1", 404);
    foreach (var correo in new[] { "admin@prueba.test", "operador@prueba.test" })
    {
        client.DefaultRequestHeaders.Authorization = new("Bearer", await Login(correo));
        await Check(HttpMethod.Get, "/api/v1/pedidos/PRUEBA-1", 403);
        await Check(HttpMethod.Post, "/api/v1/pedidos/PRUEBA-1/calificacion", 403, new { calificacion = 5 });
    }
    client.DefaultRequestHeaders.Authorization = null;
    await Check(HttpMethod.Get, "/api/v1/pedidos/PRUEBA-1", 401);
    client.DefaultRequestHeaders.Authorization = new("Bearer", valid);
    var key=app.Services.GetRequiredService<Microsoft.IdentityModel.Tokens.SymmetricSecurityKey>();
    var original = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(valid);
    foreach(var expired in new[]{true,false})
    {
        var jwt=new System.IdentityModel.Tokens.Jwt.JwtSecurityToken("LoyOrder",expired?"LoyOrder.Web":"audiencia-incorrecta",
            original.Claims.Where(c => c.Type is "sub" or "sid" or "jti" or "role" or "sv"),
            expires:DateTime.UtcNow.AddMinutes(expired?-1:5),signingCredentials:new(key,Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization=new("Bearer",new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(jwt));
        await Check(HttpMethod.Get,"/api/v1/auth/me",401);
    }
    client.DefaultRequestHeaders.Authorization=new("Bearer","token.invalido");
    await Check(HttpMethod.Get,"/api/v1/auth/me",401);

    client.DefaultRequestHeaders.Authorization = null;
    var primera = ultimaRenovacion;
    reloj.Avanzar(20);
    var respuestas = await Task.WhenAll(
        client.PostAsJsonAsync("/api/v1/auth/renovar", new { refreshToken = primera }),
        client.PostAsJsonAsync("/api/v1/auth/renovar", new { refreshToken = primera }));
    try
    {
        if (!respuestas.Select(r => (int)r.StatusCode).Order().SequenceEqual([200, 401]))
            throw new Exception("Solo una renovación simultánea debe tener éxito.");
        using var renovada = JsonDocument.Parse(await respuestas.Single(r => r.IsSuccessStatusCode).Content.ReadAsStringAsync());
        var nueva = renovada.RootElement.GetProperty("refreshToken").GetString()!;
        var vence = renovada.RootElement.GetProperty("sessionExpiresAt").GetDateTimeOffset();
        if (nueva == primera || vence != reloj.Inicio.AddMinutes(60))
            throw new Exception("Rotación o plazo máximo incorrecto.");
        Console.WriteLine($"OK {++total}: renovación concurrente rota el secreto una sola vez");
        reloj.Avanzar(25);
        using var respuesta = await client.PostAsJsonAsync("/api/v1/auth/renovar", new { refreshToken = nueva });
        respuesta.EnsureSuccessStatusCode();
        using var otra = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        nueva = otra.RootElement.GetProperty("refreshToken").GetString()!;
        if (otra.RootElement.GetProperty("idleExpiresAt").GetDateTimeOffset() != vence)
            throw new Exception("La actividad no debe prolongar la sesión más allá de su máximo.");
        reloj.Avanzar(16);
        await Check(HttpMethod.Post, "/api/v1/auth/renovar", 401, new { refreshToken = nueva });
    }
    finally { foreach (var respuesta in respuestas) respuesta.Dispose(); }

    var sesiones = app.Services.GetRequiredService<Sesiones>();
    var cuenta = (await users.PorIdAsync(1, default))!;
    var inactiva = sesiones.Crear(cuenta);
    reloj.Avanzar(31);
    await Check(HttpMethod.Post, "/api/v1/auth/renovar", 401, new { refreshToken = inactiva.RefreshToken });

    var abierta = sesiones.Crear(cuenta);
    await Check(HttpMethod.Post, "/api/v1/auth/logout", 204, new { refreshToken = abierta.RefreshToken });
    await Check(HttpMethod.Post, "/api/v1/auth/renovar", 401, new { refreshToken = abierta.RefreshToken });

    var anteriorAlCambio = sesiones.Crear(cuenta);
    users.VersionCliente++;
    await Check(HttpMethod.Post, "/api/v1/auth/renovar", 401, new { refreshToken = anteriorAlCambio.RefreshToken });
}
sealed class RelojPrueba : TimeProvider
{
    public DateTimeOffset Inicio { get; } = DateTimeOffset.UtcNow;
    private int minutos;
    public override DateTimeOffset GetUtcNow() => Inicio.AddMinutes(minutos);
    public void Avanzar(int cantidad) => minutos += cantidad;
}
sealed class UsuariosPrueba : IUsuarioRepository
{
    private readonly Dictionary<string,Usuario> usuarios=new();
    public string Hash {get;}=new LoyOrder.Api.Contrasenas().Proteger("Prueba12345!");
    public bool InactivarCliente {get;set;}
    public int VersionCliente {get;set;}
    public UsuariosPrueba()
    {
        foreach(var (correo,rol,activo) in new[]{("cliente@prueba.test",Roles.Cliente,true),("admin@prueba.test",Roles.Administrador,true),("operador@prueba.test",Roles.Operador,true),("inactivo@prueba.test",Roles.Cliente,false)})
            usuarios[correo]=new(usuarios.Count+1,rol,correo,Hash,activo,rol);
    }
    public Task<Usuario?> PorCorreoAsync(string correo,CancellationToken ct)=>Task.FromResult(usuarios.GetValueOrDefault(correo));
    public Task<Usuario?> PorIdAsync(long id,CancellationToken ct)
    {
        var u=usuarios.Values.FirstOrDefault(u=>u.IdUsuario==id);
        return Task.FromResult(u?.Correo == "cliente@prueba.test"
            ? u with { Activo = !InactivarCliente, VersionSesion = VersionCliente }
            : u);
    }
    public Task<Usuario> RegistrarClienteAsync(string n,string c,string h,string? t,CancellationToken ct, NuevoDesafio? desafio = null)
    {
        if(usuarios.ContainsKey(c))throw new CorreoDuplicadoException();
        var u=new Usuario(usuarios.Count+1,n,c,h,true,Roles.Cliente);usuarios[c]=u;return Task.FromResult(u);
    }
}
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

sealed class OrigenPrueba : IOrigenCatalogo
{
    public Exception? Falla { get; set; }
    public Task<IReadOnlyList<TiendaOrigen>> TiendasAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<TiendaOrigen>>([new("t1","Tienda")]);
    public Task<IReadOnlyList<CategoriaOrigen>> CategoriasAsync(CancellationToken ct) =>
        Falla is null ? Task.FromResult<IReadOnlyList<CategoriaOrigen>>([new("c1","Bebidas",false)]) : throw Falla;
    public Task<IReadOnlyList<ArticuloOrigen>> ArticulosAsync(string t, DateTimeOffset? desde, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ArticuloOrigen>>([new("a1","Refresco",null,"c1",null,true,false,[new("v1",null,25m,true)])]);
    public Task<IReadOnlyList<ExistenciaOrigen>> ExistenciasAsync(string t, CancellationToken ct) => Task.FromResult<IReadOnlyList<ExistenciaOrigen>>([new("v1",10m)]);
}
sealed class CatalogoPrueba : ICatalogoRepository
{
    private long siguiente;
    public RestauranteSincronizable? Restaurante { get; set; } = new(1,"t1",2);
    public Task<RestauranteSincronizable?> RestauranteAsync(long? id, CancellationToken ct) => Task.FromResult(Restaurante);
    public Task VincularTiendaAsync(long id, string tienda, CancellationToken ct) => Task.CompletedTask;
    public Task<DateTimeOffset?> UltimaCorrectaAsync(long id, string tipo, CancellationToken ct) => Task.FromResult<DateTimeOffset?>(null);
    public Task<long> IniciarRegistroAsync(long id, string tipo, DateTimeOffset inicio, CancellationToken ct) => Task.FromResult(++siguiente);
    public Task TerminarRegistroAsync(long id, string estado, DateTimeOffset fin, string? error, string? resumen, CancellationToken ct) => Task.CompletedTask;
    public Task<ResultadoCatalogo> GuardarCatalogoAsync(long id, IReadOnlyList<CategoriaGuardar> c, IReadOnlyList<ProductoGuardar> p, CancellationToken ct) =>
        Task.FromResult(new ResultadoCatalogo(c.Count, 0, p.Count, 0, 0, 1, 0));
    public Task<int> GuardarExistenciasAsync(IReadOnlyList<ExistenciaOrigen> e, CancellationToken ct) => Task.FromResult(e.Count);
    public Task<int> CategoriasSinEstacionAsync(long id, CancellationToken ct) => Task.FromResult(0);
    public Task<IReadOnlyList<RegistroSincronizacion>> HistorialAsync(long id, int n, CancellationToken ct) => Task.FromResult<IReadOnlyList<RegistroSincronizacion>>([]);
}

sealed class PedidosPrueba : IPedidoClienteRepository, IHistorialPedidoRepository
{
    public Task<IReadOnlyList<ResumenPedidoCliente>> ListarAsync(long idUsuario,int pagina,int tamano,CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ResumenPedidoCliente>>(idUsuario == 1 ? [new("PRUEBA-1","Entregado","Recolección",DateTime.UtcNow,100,0,100,null,[])] : []);

    public Task<SeguimientoPedido?> ConsultarAsync(long idUsuario, string folio, CancellationToken ct) =>
        Task.FromResult<SeguimientoPedido?>(idUsuario == 1 && folio == "PRUEBA-1"
            ? new(folio, "Entregado", "Recolección", DateTime.UtcNow, DateTime.UtcNow, 100, 0, 100, null, null, [], [])
            : null);

    public async Task CalificarAsync(long idUsuario, string folio, short calificacion, string? comentario, CancellationToken ct)
    {
        if (await ConsultarAsync(idUsuario, folio, ct) is null) throw new PedidoNoEncontradoException();
    }
}

sealed class MenuPrueba : IMenuRepository
{
    public int Fallo { get; set; }
    public List<long> Consultados { get; } = [];
    public Task<IReadOnlyList<FilaMenu>> ListarAsync(long idRestaurante, CancellationToken ct)
    {
        Consultados.Add(idRestaurante);
        if (Fallo == 1) throw new DependenciaNoDisponibleException();
        if (Fallo == 2) throw new InvalidOperationException("DETALLE_INTERNO_NO_PUBLICAR");
        return Task.FromResult<IReadOnlyList<FilaMenu>>([
            new(1, "Bebidas", 1, "Refresco", null, null, 1, "Cola", 25m, true, 48m),
            new(1, "Bebidas", 1, "Refresco", null, null, 2, "Lima-limón", 22m, true, 0m)]);
    }
}


sealed class CheckoutPrueba : ICrearPedidoRepository
{
    public Task<bool> DisponibleAsync(CancellationToken ct) => Task.FromResult(true);
    private readonly Dictionary<(long,Guid),(byte[] Hash,PedidoCreado Pedido)> pedidos = [];
    public Task<ConfiguracionPedido> ConfiguracionAsync(long? id,CancellationToken ct) =>
        Task.FromResult(new ConfiguracionPedido(1,"Prueba","t1",new(0,0),new(23,59),25,[new(1,"Efectivo")]));
    public Task<IReadOnlyList<VariantePedido>> VariantesAsync(long id,long[] ids,CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<VariantePedido>>([new(11,"v11","Prueba",100,false)]);
    public Task<PedidoCreado?> RecuperarAsync(long usuario,Guid operacion,byte[] hash,CancellationToken ct)
    {
        if (!pedidos.TryGetValue((usuario,operacion),out var anterior)) return Task.FromResult<PedidoCreado?>(null);
        if (!hash.SequenceEqual(anterior.Hash)) throw new CheckoutCambioException("Operación reutilizada");
        return Task.FromResult<PedidoCreado?>(anterior.Pedido);
    }
    public Task<PedidoCreado> GuardarAsync(long usuario,SolicitudPedido s,byte[] hash,ConfiguracionPedido c,
        IReadOnlyList<VariantePedido> v,DateTimeOffset ahora,TimeZoneInfo zona,CancellationToken ct)
    {
        var pedido = new PedidoCreado(1,"LY-00000001",ahora.UtcDateTime,"Recibido",200,0,200);
        pedidos.Add((usuario,s.IdOperacion),(hash,pedido));
        return Task.FromResult(pedido);
    }
}


sealed class OperacionPrueba : IOperacionPedidoRepository
{
    public Task<IReadOnlyList<PedidoOperativo>> ListarAsync(long operador,CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PedidoOperativo>>([new("PRUEBA-1","Recibido","Recolección",DateTime.UtcNow,100,"Cliente",[])]);
    public Task<EstadoPedidoActualizado> CambiarAsync(long operador,string folio,string estado,string? motivo,CancellationToken ct) =>
        folio == "PRUEBA-1" ? Task.FromResult(new EstadoPedidoActualizado(folio,estado)) : throw new PedidoNoEncontradoException();
}
