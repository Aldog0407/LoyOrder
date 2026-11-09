using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using LoyOrder.Api;
using LoyOrder.Application;
using LoyOrder.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT","Development");
var users = new UsuariosPrueba();
var fake = new Repositorio();
await using var app = ApiHost.Crear([], services => { services.AddSingleton<ICategoriaRepository>(fake); services.AddSingleton<IUsuarioRepository>(users); });
app.Urls.Add("http://127.0.0.1:0");
await app.StartAsync();
using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
var total = 0;
async Task<string> Login(string correo="cliente@prueba.test")
{
    using var response=await client.PostAsJsonAsync("/api/v1/auth/login",new{correo,contrasena="Prueba12345!"});
    response.EnsureSuccessStatusCode();
    return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("accessToken").GetString()!;
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
    }
    client.DefaultRequestHeaders.Authorization=new("Bearer",await Login());
    users.InactivarCliente=true;
    await Check(HttpMethod.Get,"/api/v1/auth/me",401);
    users.InactivarCliente=false;
    var valid=await Login();
    client.DefaultRequestHeaders.Authorization=new("Bearer",valid[..^10]+(valid[^10]=='a' ? "b":"a")+valid[^9..]);
    await Check(HttpMethod.Get,"/api/v1/auth/me",401);
    var key=app.Services.GetRequiredService<Microsoft.IdentityModel.Tokens.SymmetricSecurityKey>();
    foreach(var expired in new[]{true,false})
    {
        var id=Guid.NewGuid().ToString();
        app.Services.GetRequiredService<Sesiones>().Crear(id,DateTimeOffset.UtcNow.AddMinutes(5));
        var jwt=new System.IdentityModel.Tokens.Jwt.JwtSecurityToken("LoyOrder",expired?"LoyOrder.Web":"audiencia-incorrecta",
            [new("sub","1"),new("jti",id),new("role",Roles.Cliente)],expires:DateTime.UtcNow.AddMinutes(expired?-1:5),signingCredentials:new(key,Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization=new("Bearer",new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(jwt));
        await Check(HttpMethod.Get,"/api/v1/auth/me",401);
    }
    client.DefaultRequestHeaders.Authorization=new("Bearer","token.invalido");
    await Check(HttpMethod.Get,"/api/v1/auth/me",401);
}
sealed class UsuariosPrueba : IUsuarioRepository
{
    private readonly Dictionary<string,Usuario> usuarios=new();
    public string Hash {get;}=new LoyOrder.Api.Contrasenas().Proteger("Prueba12345!");
    public bool InactivarCliente {get;set;}
    public UsuariosPrueba()
    {
        foreach(var (correo,rol,activo) in new[]{("cliente@prueba.test",Roles.Cliente,true),("admin@prueba.test",Roles.Administrador,true),("operador@prueba.test",Roles.Operador,true),("inactivo@prueba.test",Roles.Cliente,false)})
            usuarios[correo]=new(usuarios.Count+1,rol,correo,Hash,activo,rol);
    }
    public Task<Usuario?> PorCorreoAsync(string correo,CancellationToken ct)=>Task.FromResult(usuarios.GetValueOrDefault(correo));
    public Task<Usuario?> PorIdAsync(long id,CancellationToken ct)
    {
        var u=usuarios.Values.FirstOrDefault(u=>u.IdUsuario==id);
        return Task.FromResult(u?.Correo=="cliente@prueba.test"&&InactivarCliente ? u with{Activo=false} :u);
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
