using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json.Serialization;
using LoyOrder.Application;
using LoyOrder.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace LoyOrder.Api;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Registro([Required,StringLength(100)]string Nombre,[Required,EmailAddress,StringLength(150)]string Correo,[Required,StringLength(128,MinimumLength=8)]string Contrasena,[StringLength(20)]string? Telefono);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Inicio([Required,EmailAddress,StringLength(150)]string Correo,[Required,StringLength(128)]string Contrasena);
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
    [HttpGet("me"),Authorize]
    public async Task<IActionResult> Actual(CancellationToken ct)
    {
        var usuario=(await usuarios.PorIdAsync(long.Parse(User.FindFirstValue("sub")!),ct))!;
        return Ok(new {usuario.IdUsuario,usuario.Nombre,usuario.Correo,usuario.Rol});
    }
    [HttpPost("logout"),Authorize]
    public IActionResult Cerrar(){sesiones.Cerrar(User.FindFirstValue("jti")!);return NoContent();}
}
