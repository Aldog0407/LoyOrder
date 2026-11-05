using System.ComponentModel.DataAnnotations;
using LoyOrder.Application;
using Microsoft.AspNetCore.Mvc;
namespace LoyOrder.Api;
[ApiController]
[Route("api/v1/categorias")]
public sealed class CategoriasController(ConsultarCategorias consulta) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery, Required, Range(1, long.MaxValue)] long? idRestaurante,
        [FromQuery, Range(1, 1000000)] int pagina = 1,
        [FromQuery, Range(1, 100)] int tamano = 20, CancellationToken ct = default)
        => Ok(await consulta.EjecutarAsync(idRestaurante!.Value, pagina, tamano, ct));
}

