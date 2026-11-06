using System.Security.Claims;
using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using LoyOrder.Application;
using LoyOrder.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
namespace LoyOrder.Api;
public sealed class Contrasenas : IContrasenas
{
    private readonly PasswordHasher<string> hasher = new();
    public string Proteger(string clave) => hasher.HashPassword("",clave);
    public bool Verificar(string hash,string clave)
    { try{return hasher.VerifyHashedPassword("",hash,clave)!=PasswordVerificationResult.Failed;}catch(FormatException){return false;} }
}
// Sesiones en memoria para UNA instancia de API. Al reiniciar, todas caducan.
// Para escalar a varias instancias, sustituir por un almacén compartido acordado con el equipo.
public sealed class Sesiones(IMemoryCache cache)
{
    public void Crear(string id, DateTimeOffset vence) => cache.Set("sesion:"+id,true,vence);
    public bool Existe(string id) => cache.TryGetValue("sesion:"+id,out _);
    public void Cerrar(string id) => cache.Remove("sesion:"+id);
}
public sealed class Tokens(SymmetricSecurityKey key, Sesiones sesiones)
{
    public object Emitir(Usuario usuario)
    {
        var vence=DateTimeOffset.UtcNow.AddMinutes(30);
        var id=Guid.NewGuid().ToString("N");
        var jwt=new JwtSecurityToken("LoyOrder","LoyOrder.Web",
            [new("sub",usuario.IdUsuario.ToString()),new("jti",id),new("role",usuario.Rol)],
            expires:vence.UtcDateTime,signingCredentials:new(key,SecurityAlgorithms.HmacSha256));
        sesiones.Crear(id,vence);
        return new { accessToken=new JwtSecurityTokenHandler().WriteToken(jwt),tokenType="Bearer",expiresAt=vence,usuario=new { usuario.IdUsuario,usuario.Nombre,usuario.Correo,usuario.Rol } };
    }
}
public static class ConfigurarAcceso
{
    public static void AgregarAcceso(this WebApplicationBuilder builder)
    {
        var texto=builder.Configuration["Auth:SigningKey"];
        byte[] bytes;
        if(string.IsNullOrWhiteSpace(texto))
        {
            if(!builder.Environment.IsDevelopment()) throw new InvalidOperationException("Configure Auth__SigningKey (Base64 de al menos 32 bytes).");
            bytes=RandomNumberGenerator.GetBytes(32); // Solo desarrollo, no se persiste.
        }
        else
        {
            try{bytes=Convert.FromBase64String(texto);}catch(FormatException){throw new InvalidOperationException("Auth__SigningKey debe ser Base64.");}
            if(bytes.Length<32)throw new InvalidOperationException("Auth__SigningKey requiere al menos 32 bytes.");
        }
        var key=new SymmetricSecurityKey(bytes);
        builder.Services.AddSingleton(key);
        builder.Services.AddMemoryCache();builder.Services.AddSingleton<Sesiones>();builder.Services.AddSingleton<Tokens>();
        builder.Services.AddSingleton<IContrasenas,Contrasenas>();builder.Services.AddScoped<Acceso>();
        builder.Services.AddScoped<IUsuarioRepository,LoyOrder.Infrastructure.Persistence.UsuarioRepository>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o=>
        {
            o.MapInboundClaims=false;
            o.TokenValidationParameters=new(){ValidateIssuer=true,ValidIssuer="LoyOrder",ValidateAudience=true,ValidAudience="LoyOrder.Web",ValidateLifetime=true,ValidateIssuerSigningKey=true,IssuerSigningKey=key,ClockSkew=TimeSpan.Zero,RoleClaimType="role",NameClaimType="sub",ValidAlgorithms=[SecurityAlgorithms.HmacSha256]};
            o.Events=new JwtBearerEvents{OnTokenValidated=async context=>
            {
                var id=context.Principal?.FindFirstValue("jti");
                var sub=context.Principal?.FindFirstValue("sub");
                if(id is null || !long.TryParse(sub,out var userId) || !context.HttpContext.RequestServices.GetRequiredService<Sesiones>().Existe(id))
                {context.Fail("Sesión inválida.");return;}
                var usuario=await context.HttpContext.RequestServices.GetRequiredService<IUsuarioRepository>().PorIdAsync(userId,context.HttpContext.RequestAborted);
                if(usuario is null || !usuario.Activo || usuario.Rol!=context.Principal?.FindFirstValue("role")) context.Fail("Cuenta o permisos modificados.");
            }};
        });
        builder.Services.AddAuthorization();
        builder.Services.AddCors(o=>o.AddPolicy("Frontend",p=>p.WithOrigins("http://localhost:5173","http://127.0.0.1:5173").AllowAnyHeader().WithMethods("GET","POST")));
        builder.Services.AddRateLimiter(o=>
        {
            o.RejectionStatusCode=429;
            o.AddPolicy("acceso",context=>System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"local",_=>new(){PermitLimit=20,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
        });
    }
}
