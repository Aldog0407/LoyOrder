using System.Globalization;
using System.Security.Claims;
using LoyOrder.Application;
using LoyOrder.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LoyOrder.Api;

[ApiController, Route("api/v1/admin/menu"), Authorize(Roles = Roles.Administrador)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ErrorApi), 401)]
[ProducesResponseType(typeof(ErrorApi), 403)]
[ProducesResponseType(typeof(ErrorApi), 503)]
public sealed class EdicionMenuController(EditarMenu menu) : ControllerBase
{
    private long Administrador => long.Parse(User.FindFirstValue("sub")!, CultureInfo.InvariantCulture);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductoEditable>>> Listar(CancellationToken ct) =>
        Ok(await menu.ListarAsync(Administrador, ct));

    [HttpPost("{id:long}/editar")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ErrorApi), 400)]
    [ProducesResponseType(typeof(ErrorApi), 404)]
    public async Task<IActionResult> Guardar(long id, DatosMenuLocal datos, CancellationToken ct)
    {
        await menu.GuardarAsync(Administrador, id, datos, ct);
        return NoContent();
    }
}
