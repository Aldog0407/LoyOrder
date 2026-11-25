namespace LoyOrder.Application;

// Actividad 7.2.4 · Catálogo por categorías (req. 8, CU-03 pasos 1 y 2).
// El cliente ve lo que la sincronización (7.2.3) copió de Loyverse. Reglas:
//  - Solo categorías activas y productos disponibles con al menos una variante disponible y con precio mayor a 0.
//    Lo que Loyverse marca como no disponible en la tienda (por ejemplo la hawaiana) no se muestra.
//  - Una variante que controla inventario y tiene 0 existencias o menos se muestra como agotada y no se puede
//    agregar al carrito (por ejemplo el refresco lima-limón). Un producto con todas sus variantes agotadas queda agotado.
//  - Las existencias desconocidas (null, aún sin sincronizar el inventario) no marcan la variante como agotada:
//    la validación definitiva se hace al confirmar el pedido contra Loyverse (7.2.6, CU-03 RN-04).

/// <summary>Una fila del catálogo vendible: variante con su producto y su categoría, en el orden en que se muestra.</summary>
public sealed record FilaMenu(long IdCategoria, string Categoria, long IdProducto, string Producto, string? Descripcion,
    string? ImagenUrl, long IdVariante, string? Variante, decimal Precio, bool ControlaInventario, decimal? Existencias);

public interface IMenuRepository
{
    /// <summary>
    /// Variantes vendibles del restaurante (categoría activa, producto y variante disponibles, precio &gt; 0),
    /// ordenadas por categoría ("Sin categoría" al final), producto y variante.
    /// </summary>
    Task<IReadOnlyList<FilaMenu>> ListarAsync(long idRestaurante, CancellationToken ct);
}

public sealed record VarianteMenu(long Id, string? Nombre, decimal Precio, bool Agotado);
public sealed record ProductoMenu(long Id, string Nombre, string? Descripcion, string? ImagenUrl, decimal PrecioDesde,
    bool Agotado, IReadOnlyList<VarianteMenu> Variantes);
public sealed record CategoriaMenu(long Id, string Nombre, IReadOnlyList<ProductoMenu> Productos);
/// <param name="ActualizadoEn">Inicio de la última sincronización de inventario correcta; null si nunca se ha sincronizado.</param>
public sealed record MenuCliente(DateTimeOffset? ActualizadoEn, IReadOnlyList<CategoriaMenu> Categorias);

public sealed class ConsultarMenu(IMenuRepository menu, ICatalogoRepository catalogo)
{
    /// <param name="idRestaurante">El de <c>Sincronizacion:IdRestaurante</c>; si es null, el primer restaurante registrado.</param>
    /// <exception cref="RestauranteNoConfiguradoException">No hay restaurante registrado (la API responde 409).</exception>
    public async Task<MenuCliente> EjecutarAsync(long? idRestaurante, CancellationToken ct)
    {
        var restaurante = await catalogo.RestauranteAsync(idRestaurante, ct)
            ?? throw new RestauranteNoConfiguradoException("No hay un restaurante registrado.");
        var filas = await menu.ListarAsync(restaurante.IdRestaurante, ct);
        var actualizado = await catalogo.UltimaCorrectaAsync(restaurante.IdRestaurante, TiposSincronizacion.Inventario, ct);
        return new MenuCliente(actualizado, Agrupar(filas));
    }

    public static bool EstaAgotada(FilaMenu fila) => fila.ControlaInventario && fila.Existencias is <= 0m;

    /// <summary>Agrupa las filas por categoría y producto conservando el orden del repositorio.</summary>
    public static IReadOnlyList<CategoriaMenu> Agrupar(IReadOnlyList<FilaMenu> filas) =>
        filas.Where(f => f.Precio > 0)
            .GroupBy(f => (f.IdCategoria, f.Categoria))
            .Select(c => new CategoriaMenu(c.Key.IdCategoria, c.Key.Categoria, c
                .GroupBy(f => f.IdProducto)
                .Select(p =>
                {
                    var primera = p.First();
                    var variantes = p.Select(f => new VarianteMenu(f.IdVariante, string.IsNullOrWhiteSpace(f.Variante) ? null : f.Variante.Trim(),
                        f.Precio, EstaAgotada(f))).ToList();
                    var agotado = variantes.All(v => v.Agotado);
                    var desde = variantes.Where(v => agotado || !v.Agotado).Min(v => v.Precio);
                    return new ProductoMenu(p.Key, primera.Producto, primera.Descripcion, primera.ImagenUrl, desde, agotado, variantes);
                }).ToList()))
            .ToList();
}
