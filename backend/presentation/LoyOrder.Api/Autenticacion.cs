using System.Security.Claims;
using System.Globalization;
using System.Security.Cryptography;
using LoyOrder.Application;
using LoyOrder.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
namespace LoyOrder.Api;
public sealed class Contrasenas : IContrasenas
{
    private readonly PasswordHasher<string> hasher = new();
    public string Proteger(string clave) => hasher.HashPassword("",clave);
    public bool Verificar(string hash,string clave)
    { try{return hasher.VerifyHashedPassword("",hash,clave)!=PasswordVerificationResult.Failed;}catch(FormatException){return false;} }
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
        var plazos = new PlazosSesion(
            builder.Configuration.GetValue("Auth:AccesoMinutos", 15),
            builder.Configuration.GetValue("Auth:InactividadMinutos", 30),
            builder.Configuration.GetValue("Auth:SesionMaximaMinutos", 480));
        if (plazos.AccesoMinutos < 1 || plazos.AccesoMinutos > plazos.InactividadMinutos ||
            plazos.InactividadMinutos > plazos.MaximoMinutos || plazos.MaximoMinutos > 1440)
            throw new InvalidOperationException("Los plazos de Auth deben cumplir: 1 <= acceso <= inactividad <= sesión máxima <= 1440 minutos.");

        builder.Services.AddSingleton(key);
        builder.Services.AddSingleton(plazos);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddMemoryCache();builder.Services.AddSingleton<Sesiones>();builder.Services.AddSingleton<Tokens>();
        builder.Services.AddSingleton<IContrasenas,Contrasenas>();builder.Services.AddScoped<Acceso>();
        builder.Services.AddScoped<IUsuarioRepository,LoyOrder.Infrastructure.Persistence.UsuarioRepository>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o=>
        {
            o.MapInboundClaims=false;
            o.TokenValidationParameters=new(){ValidateIssuer=true,ValidIssuer="LoyOrder",ValidateAudience=true,ValidAudience="LoyOrder.Web",ValidateLifetime=true,ValidateIssuerSigningKey=true,IssuerSigningKey=key,ClockSkew=TimeSpan.Zero,RoleClaimType="role",NameClaimType="sub",ValidAlgorithms=[SecurityAlgorithms.HmacSha256]};
            o.Events=new JwtBearerEvents{OnTokenValidated=async context=>
            {
                var servicios = context.HttpContext.RequestServices;
                var sesiones = servicios.GetRequiredService<Sesiones>();
                if (!Guid.TryParseExact(context.Principal?.FindFirstValue("sid"), "N", out var id) ||
                    sesiones.Consultar(id) is not { } estado ||
                    context.Principal?.FindFirstValue("sub") != estado.IdUsuario.ToString(CultureInfo.InvariantCulture) ||
                    context.Principal?.FindFirstValue("role") != estado.Rol ||
                    context.Principal?.FindFirstValue("sv") != estado.VersionSesion.ToString(CultureInfo.InvariantCulture))
                {
                    context.Fail("Sesión inválida.");
                    return;
                }
                var usuario = await servicios.GetRequiredService<IUsuarioRepository>()
                    .PorIdAsync(estado.IdUsuario, context.HttpContext.RequestAborted);
                var exigeCorreo = servicios.GetService<ConfiguracionDesafios>() is not null;
                if (!Tokens.CuentaValida(usuario, estado, exigeCorreo) || sesiones.Consultar(id) is null)
                    context.Fail("Cuenta o permisos modificados.");
            }};
        });
        builder.Services.AddAuthorization();
        builder.Services.AddCors(o=>o.AddPolicy("Frontend",p=>p.WithOrigins("http://localhost:5173","http://127.0.0.1:5173").AllowAnyHeader().WithMethods("GET","POST")));
        builder.Services.AddRateLimiter(o=>
        {
            o.RejectionStatusCode=429;
            o.AddPolicy("renovacion", context =>
                System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "local",
                    _ => new() { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.AddPolicy("acceso",context=>System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"local",_=>new(){PermitLimit=20,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
        });
    }
}
