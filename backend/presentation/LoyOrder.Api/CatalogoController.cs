using LoyOrder.Application;
using LoyOrder.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LoyOrder.Api;

/// <summary>
/// Actividad 7.2.4 · Catálogo por categorías (req. 8). Un solo restaurante: se usa el de
/// <c>Sincronizacion:IdRestaurante</c> o, si no está configurado, el primero registrado (igual que la 7.2.3).
/// 200 con el menú (lista vacía si aún no se sincroniza); 409 si no hay restaurante; 503 sin base de datos.
/// </summary>
[ProducesResponseType(typeof(ErrorApi),401)]
[ProducesResponseType(typeof(ErrorApi),403)]
[ProducesResponseType(typeof(ErrorApi),409)]
[ProducesResponseType(typeof(ErrorApi),502)]
[ProducesResponseType(typeof(ErrorApi),503)]
[ApiController, Route("api/v1/catalogo"), Authorize(Roles = Roles.Cliente)]
public sealed class CatalogoController(ConsultarMenu consulta, IConfiguration configuracion) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MenuCliente>> Obtener(CancellationToken ct) =>
        Ok(await consulta.EjecutarAsync(ConfigurarSincronizacion.IdRestaurante(configuracion), ct));
}
