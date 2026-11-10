using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json.Serialization;
using LoyOrder.Application;
using LoyOrder.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
namespace LoyOrder.Api;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Registro([Required,StringLength(100)]string Nombre,[Required,EmailAddress,StringLength(150)]string Correo,[Required,StringLength(128,MinimumLength=8)]string Contrasena,[StringLength(20)]string? Telefono);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Inicio([Required,EmailAddress,StringLength(150)]string Correo,[Required,StringLength(128)]string Contrasena);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RenovarSesion([Required, StringLength(97, MinimumLength = 97)] string RefreshToken);
[ApiController,Route("api/v1/auth")]
public sealed class AccesoController(Acceso acceso,Tokens tokens,Sesiones sesiones,IUsuarioRepository usuarios,
    CorreoAcceso? correo = null) : ControllerBase
{
    [HttpPost("registro"),AllowAnonymous,EnableRateLimiting("acceso")]
    public async Task<IActionResult> Registrar(Registro datos,CancellationToken ct)
    {
        if (correo is not null)
        {
            var (pendiente, idDesafio) = await correo.RegistrarAsync(datos.Nombre, datos.Correo, datos.Contrasena, datos.Telefono, ct);
            return StatusCode(201, new { pendiente.IdUsuario, pendiente.Nombre, pendiente.Correo, pendiente.Rol, correoVerificado = false, idDesafio });
        }
        var usuario=await acceso.RegistrarAsync(datos.Nombre,datos.Correo,datos.Contrasena,datos.Telefono,ct);
        return StatusCode(201,new { usuario.IdUsuario,usuario.Nombre,usuario.Correo,usuario.Rol });
    }
    [HttpPost("login"),AllowAnonymous,EnableRateLimiting("acceso")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Iniciar(Inicio datos,CancellationToken ct)
    {
        var usuario = await acceso.IniciarAsync(datos.Correo, datos.Contrasena, ct);
        if (correo is not null && usuario.CorreoVerificadoEn is null)
        {
            var idDesafio = await correo.VerificarAsync(usuario, ct);
            return Problem(statusCode: 403, title: "Verifica tu correo para iniciar sesión.",
                extensions: new Dictionary<string, object?> { ["codigo"] = "CORREO_NO_VERIFICADO", ["idDesafio"] = idDesafio });
        }
        return Ok(tokens.Emitir(usuario));
    }
    [HttpPost("renovar"), AllowAnonymous, EnableRateLimiting("renovacion")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Renovar(RenovarSesion datos, CancellationToken ct)
    {
        var estado = sesiones.ConsultarRenovacion(datos.RefreshToken);
        if (estado is null) return Unauthorized();
        var usuario = await usuarios.PorIdAsync(estado.IdUsuario, ct);
        if (!Tokens.CuentaValida(usuario, estado, correo is not null))
        {
            sesiones.Cerrar(estado.Id);
            return Unauthorized();
        }
        var credencial = sesiones.Renovar(datos.RefreshToken);
        return credencial is null ? Unauthorized() : Ok(tokens.Emitir(usuario!, credencial));
    }
    [HttpGet("me"),Authorize]
    public async Task<IActionResult> Actual(CancellationToken ct)
    {
        var usuario=(await usuarios.PorIdAsync(long.Parse(User.FindFirstValue("sub")!),ct))!;
        return Ok(new {usuario.IdUsuario,usuario.Nombre,usuario.Correo,usuario.Rol});
    }
    [HttpPost("logout"), AllowAnonymous, EnableRateLimiting("renovacion")]
    public IActionResult Cerrar([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RenovarSesion? datos)
    {
        if (User.Identity?.IsAuthenticated == true && Guid.TryParseExact(User.FindFirstValue("sid"), "N", out var id))
        {
            sesiones.Cerrar(id);
            return NoContent();
        }
        return datos is not null && sesiones.CerrarRenovacion(datos.RefreshToken) ? NoContent() : Unauthorized();
    }
}
