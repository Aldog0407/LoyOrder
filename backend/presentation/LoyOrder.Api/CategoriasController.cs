using System.ComponentModel.DataAnnotations;
using LoyOrder.Application;
using Microsoft.AspNetCore.Mvc;
namespace LoyOrder.Api;
[Microsoft.AspNetCore.Authorization.Authorize(Roles = LoyOrder.Domain.Roles.Cliente)]
[ProducesResponseType(typeof(ErrorApi),400)]
[ProducesResponseType(typeof(ErrorApi),401)]
[ProducesResponseType(typeof(ErrorApi),403)]
[ProducesResponseType(typeof(ErrorApi),404)]
[ProducesResponseType(typeof(ErrorApi),409)]
[ProducesResponseType(typeof(ErrorApi),429)]
[ProducesResponseType(typeof(ErrorApi),500)]
[ProducesResponseType(typeof(ErrorApi),503)]
[ApiController]
[Route("api/v1/categorias")]
public sealed class CategoriasController(ConsultarCategorias consulta) : ControllerBase
{
    [Microsoft.AspNetCore.Http.EndpointSummary("Consultar categorías paginadas del restaurante")]
    [ProducesResponseType(typeof(IReadOnlyList<LoyOrder.Domain.Categoria>), 200)]
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery, Required, Range(1, long.MaxValue)] long? idRestaurante,
        [FromQuery, Range(1, 1000000)] int pagina = 1,
        [FromQuery, Range(1, 100)] int tamano = 20, CancellationToken ct = default)
        => Ok(await consulta.EjecutarAsync(idRestaurante!.Value, pagina, tamano, ct));
}

