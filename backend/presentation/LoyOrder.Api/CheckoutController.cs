using System.Globalization;
using System.Security.Claims;
using LoyOrder.Application;
using LoyOrder.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LoyOrder.Api;

[ApiController, Route("api/v1/pedidos"), Authorize(Roles = Roles.Cliente)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CheckoutController(CrearPedido crear, IConfiguration configuracion) : ControllerBase
{
    [HttpGet("opciones")]
    public async Task<IActionResult> Opciones(CancellationToken ct) =>
        Ok(await crear.OpcionesAsync(ConfigurarSincronizacion.IdRestaurante(configuracion), ct));

    [HttpPost]
    public async Task<IActionResult> Confirmar(SolicitudPedido datos, CancellationToken ct)
    {
        var id = long.Parse(User.FindFirstValue("sub")!, CultureInfo.InvariantCulture);
        var pedido = await crear.EjecutarAsync(id, ConfigurarSincronizacion.IdRestaurante(configuracion), datos, ct);
        return Created("/api/v1/pedidos/" + Uri.EscapeDataString(pedido.Folio), pedido);
    }
}
