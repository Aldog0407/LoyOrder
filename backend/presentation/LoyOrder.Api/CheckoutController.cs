using System.Globalization;
using System.Security.Claims;
using LoyOrder.Application;
using LoyOrder.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LoyOrder.Api;

[ProducesResponseType(typeof(ErrorApi),400)]
[ProducesResponseType(typeof(ErrorApi),401)]
[ProducesResponseType(typeof(ErrorApi),403)]
[ProducesResponseType(typeof(ErrorApi),404)]
[ProducesResponseType(typeof(ErrorApi),409)]
[ProducesResponseType(typeof(ErrorApi),429)]
[ProducesResponseType(typeof(ErrorApi),500)]
[ProducesResponseType(typeof(ErrorApi),503)]
[ApiController, Route("api/v1/pedidos"), Authorize(Roles = Roles.Cliente)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CheckoutController(CrearPedido crear, IConfiguration configuracion) : ControllerBase
{
    [Microsoft.AspNetCore.Http.EndpointSummary("Consultar horario, envío y métodos de pago")]
    [ProducesResponseType(typeof(OpcionesPedido), 200)]
    [HttpGet("opciones")]
    public async Task<IActionResult> Opciones(CancellationToken ct) =>
        Ok(await crear.OpcionesAsync(ConfigurarSincronizacion.IdRestaurante(configuracion), ct));

    [Microsoft.AspNetCore.Http.EndpointSummary("Crear un pedido o recuperar el mismo intento sin duplicarlo")]
    [ProducesResponseType(typeof(PedidoCreado), 201)]
    [Microsoft.AspNetCore.Http.EndpointDescription("tipoEntrega admite Recolección o Domicilio. Usa los ID del catálogo y de /pedidos/opciones. Conserva idOperacion al reintentar la misma compra. Una compra nueva requiere otro UUID. Se validan 1–20 unidades por variante y los precios guardados en el servidor.")]
    [HttpPost]
    public async Task<IActionResult> Confirmar(SolicitudPedido datos, CancellationToken ct)
    {
        var id = long.Parse(User.FindFirstValue("sub")!, CultureInfo.InvariantCulture);
        var pedido = await crear.EjecutarAsync(id, ConfigurarSincronizacion.IdRestaurante(configuracion), datos, ct);
        return Created("/api/v1/pedidos/" + Uri.EscapeDataString(pedido.Folio), pedido);
    }
}
