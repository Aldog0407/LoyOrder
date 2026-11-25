using LoyOrder.Application;
using LoyOrder.Infrastructure.Loyverse.Modelos;

namespace LoyOrder.Infrastructure.Loyverse;

/// <summary>
/// Adaptador de la sincronización (7.2.3, CU-10): traduce lo que devuelve Loyverse a los datos de la capa de aplicación
/// y convierte sus errores en errores que la aplicación entiende, sin exponer respuestas de Loyverse.
/// </summary>
public sealed class OrigenCatalogoLoyverse(ILoyverseClient loyverse) : IOrigenCatalogo
{
    public Task<IReadOnlyList<TiendaOrigen>> TiendasAsync(CancellationToken ct) => Traducir(async () =>
        (IReadOnlyList<TiendaOrigen>)(await loyverse.ObtenerTiendasAsync(ct))
            .Where(t => t.EliminadaEn is null).Select(t => new TiendaOrigen(t.Id, t.Nombre)).ToList(), ct);

    public Task<IReadOnlyList<CategoriaOrigen>> CategoriasAsync(CancellationToken ct) => Traducir(async () =>
        (IReadOnlyList<CategoriaOrigen>)(await loyverse.ObtenerCategoriasAsync(incluirEliminadas: true, ct: ct))
            .Select(c => new CategoriaOrigen(c.Id, c.Nombre, c.EliminadaEn is not null)).ToList(), ct);

    public Task<IReadOnlyList<ArticuloOrigen>> ArticulosAsync(string idTienda, DateTimeOffset? desde, CancellationToken ct) => Traducir(async () =>
        (IReadOnlyList<ArticuloOrigen>)(await loyverse.ObtenerArticulosAsync(desde, incluirEliminados: true, ct: ct))
            .Select(a => Convertir(a, idTienda)).ToList(), ct);

    public Task<IReadOnlyList<ExistenciaOrigen>> ExistenciasAsync(string idTienda, CancellationToken ct) => Traducir(async () =>
        (IReadOnlyList<ExistenciaOrigen>)(await loyverse.ObtenerExistenciasAsync(idTienda, ct))
            .Where(n => string.IsNullOrEmpty(n.TiendaId) || n.TiendaId == idTienda)
            .GroupBy(n => n.VarianteId)
            .Select(g => new ExistenciaOrigen(g.Key, g.Sum(n => n.Existencias))).ToList(), ct);

    /// <summary>Precio y disponibilidad salen de la tienda vinculada; si la variante no está en esa tienda, no se vende en línea.</summary>
    public static ArticuloOrigen Convertir(Articulo a, string idTienda) => new(
        a.Id, a.Nombre, a.Descripcion, a.CategoriaId, a.UrlImagen, a.ControlaInventario, a.EliminadoEn is not null,
        a.Variantes.Select(v =>
        {
            var enTienda = v.EnTienda(idTienda);
            return new VarianteOrigen(v.Id, v.ValorOpcion1,
                enTienda is null ? null : enTienda.Precio ?? v.PrecioBase,
                enTienda?.DisponibleParaVenta ?? false);
        }).ToList());

    private static async Task<T> Traducir<T>(Func<Task<T>> accion, CancellationToken ct)
    {
        try { return await accion(); }
        catch (LoyverseNoConfiguradoException) { throw new OrigenNoConfiguradoException(); }
        catch (LoyverseNoAutorizadoException) { throw new OrigenNoAutorizadoException(); }
        catch (LoyverseException) { throw new OrigenNoDisponibleException(); }
        catch (HttpRequestException) { throw new OrigenNoDisponibleException(); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new OrigenNoDisponibleException(); }
    }
}
