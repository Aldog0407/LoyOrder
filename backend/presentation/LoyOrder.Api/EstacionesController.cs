using System.Globalization;
using System.Security.Claims;
using LoyOrder.Application;
using LoyOrder.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LoyOrder.Api;

[ApiController, Route("api/v1/estaciones"), Authorize(Roles=Roles.Administrador)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ErrorApi),400)]
[ProducesResponseType(typeof(ErrorApi),401)]
[ProducesResponseType(typeof(ErrorApi),403)]
[ProducesResponseType(typeof(ErrorApi),404)]
[ProducesResponseType(typeof(ErrorApi),409)]
[ProducesResponseType(typeof(ErrorApi),503)]
public sealed class EstacionesController(Estaciones estaciones) : ControllerBase
{
    private long Admin => long.Parse(User.FindFirstValue("sub")!,CultureInfo.InvariantCulture);
    [HttpGet]
    [ProducesResponseType(typeof(ConfiguracionEstaciones),200)]
    public async Task<IActionResult> Consultar(CancellationToken ct) => Ok(await estaciones.ConsultarAsync(Admin,ct));
    [HttpPost("impresoras")]
    public async Task<IActionResult> CrearImpresora(GuardarImpresora datos,CancellationToken ct) => StatusCode(201,new { id=await estaciones.GuardarImpresoraAsync(Admin,null,datos,ct) });
    [HttpPost("impresoras/{id:long}/editar")]
    public async Task<IActionResult> EditarImpresora(long id,GuardarImpresora datos,CancellationToken ct) => Ok(new { id=await estaciones.GuardarImpresoraAsync(Admin,id,datos,ct) });
    [HttpPost]
    public async Task<IActionResult> Crear(GuardarEstacion datos,CancellationToken ct) => StatusCode(201,new { id=await estaciones.GuardarEstacionAsync(Admin,null,datos,ct) });
    [HttpPost("{id:long}/editar")]
    public async Task<IActionResult> Editar(long id,GuardarEstacion datos,CancellationToken ct) => Ok(new { id=await estaciones.GuardarEstacionAsync(Admin,id,datos,ct) });
    [HttpPost("{id:long}/asignaciones")]
    public async Task<IActionResult> Asignar(long id,GuardarAsignacion datos,CancellationToken ct) => StatusCode(201,new { id=await estaciones.AsignarAsync(Admin,id,datos,ct) });
    [HttpPost("{id:long}/asignaciones/{asignacion:long}/retirar")]
    public async Task<IActionResult> Retirar(long id,long asignacion,CancellationToken ct)
    {
        await estaciones.RetirarAsync(Admin,id,asignacion,ct);
        return NoContent();
    }
}
