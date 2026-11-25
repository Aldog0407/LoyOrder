using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace LoyOrder.Application;

// Actividad 7.2.3 · CU-10 Sincronizar catálogo e inventario con Loyverse (req. 5 y 6).
// Loyverse es la fuente de verdad: LoyOrder copia categorías, productos, variantes, precios, disponibilidad y
// existencias de la tienda vinculada, y nunca escribe el catálogo en Loyverse.

// ------------------------------------------------------------------ datos que entrega el punto de venta

/// <summary>Variante con el precio y la disponibilidad que tiene en la tienda vinculada.</summary>
public sealed record VarianteOrigen(string Id, string? Nombre, decimal? Precio, bool DisponibleEnTienda);
public sealed record ArticuloOrigen(string Id, string Nombre, string? Descripcion, string? CategoriaId, string? UrlImagen,
    bool ControlaInventario, bool Eliminado, IReadOnlyList<VarianteOrigen> Variantes);
public sealed record CategoriaOrigen(string Id, string Nombre, bool Eliminada);
public sealed record ExistenciaOrigen(string VarianteId, decimal Existencias);
public sealed record TiendaOrigen(string Id, string Nombre);

/// <summary>Punto de venta del que se copia el catálogo. Lo implementa el adaptador de Loyverse.</summary>
public interface IOrigenCatalogo
{
    Task<IReadOnlyList<TiendaOrigen>> TiendasAsync(CancellationToken ct);
    /// <summary>Todas las categorías, incluidas las eliminadas.</summary>
    Task<IReadOnlyList<CategoriaOrigen>> CategoriasAsync(CancellationToken ct);
    /// <summary>Artículos modificados desde <paramref name="desde"/> (todos si es null), incluidos los eliminados.</summary>
    Task<IReadOnlyList<ArticuloOrigen>> ArticulosAsync(string idTienda, DateTimeOffset? desde, CancellationToken ct);
    Task<IReadOnlyList<ExistenciaOrigen>> ExistenciasAsync(string idTienda, CancellationToken ct);
}

// Errores del punto de venta, sin depender de Loyverse (la API los traduce a respuestas HTTP).
public sealed class OrigenNoConfiguradoException : Exception;
public sealed class OrigenNoAutorizadoException : Exception;
public sealed class OrigenNoDisponibleException : Exception;
public sealed class SincronizacionEnCursoException : Exception;
public sealed class RestauranteNoConfiguradoException(string mensaje) : Exception(mensaje);

// ------------------------------------------------------------------ datos que se guardan en LoyOrder

public sealed record RestauranteSincronizable(long IdRestaurante, string? IdTiendaLoyverse, int IntervaloMinutos);
public sealed record CategoriaGuardar(string IdLoyverse, string Nombre, bool Activo);
public sealed record VarianteGuardar(string IdLoyverse, string? Nombre, decimal Precio, bool ControlaInventario, bool Disponible);
public sealed record ProductoGuardar(string IdLoyverse, string IdCategoriaLoyverse, string Nombre, string? Descripcion,
    string? ImagenUrl, bool Disponible, bool Eliminado, IReadOnlyList<VarianteGuardar> Variantes);

public sealed record ResultadoCatalogo(int CategoriasNuevas, int CategoriasActualizadas, int ProductosNuevos,
    int ProductosActualizados, int ProductosEliminados, int VariantesNuevas, int VariantesActualizadas);

public sealed record RegistroSincronizacion(long IdSincronizacion, string Tipo, DateTimeOffset FechaInicio,
    DateTimeOffset? FechaFin, string Estado, string? DetalleError, string? Resumen);

/// <summary>Persistencia del catálogo sincronizado (PostgreSQL).</summary>
public interface ICatalogoRepository
{
    /// <summary>Restaurante a sincronizar: el indicado o, si es null, el primero registrado.</summary>
    Task<RestauranteSincronizable?> RestauranteAsync(long? idRestaurante, CancellationToken ct);
    Task VincularTiendaAsync(long idRestaurante, string idTienda, CancellationToken ct);
    Task<DateTimeOffset?> UltimaCorrectaAsync(long idRestaurante, string tipo, CancellationToken ct);
    Task<long> IniciarRegistroAsync(long idRestaurante, string tipo, DateTimeOffset inicio, CancellationToken ct);
    Task TerminarRegistroAsync(long idSincronizacion, string estado, DateTimeOffset fin, string? error, string? resumenJson, CancellationToken ct);
    /// <summary>Guarda categorías y productos en una sola transacción (inserta o actualiza por id de Loyverse).</summary>
    Task<ResultadoCatalogo> GuardarCatalogoAsync(long idRestaurante, IReadOnlyList<CategoriaGuardar> categorias,
        IReadOnlyList<ProductoGuardar> productos, CancellationToken ct);
    /// <summary>Actualiza las existencias por id de variante de Loyverse; devuelve cuántas variantes cambiaron.</summary>
    Task<int> GuardarExistenciasAsync(IReadOnlyList<ExistenciaOrigen> existencias, CancellationToken ct);
    Task<int> CategoriasSinEstacionAsync(long idRestaurante, CancellationToken ct);
    Task<IReadOnlyList<RegistroSincronizacion>> HistorialAsync(long idRestaurante, int cantidad, CancellationToken ct);
}

// ------------------------------------------------------------------ resultado

public sealed record ResumenSincronizacion(
    long IdRestaurante, string IdTiendaLoyverse, bool Completa, DateTimeOffset Inicio, DateTimeOffset Fin,
    int CategoriasNuevas, int CategoriasActualizadas, int ProductosNuevos, int ProductosActualizados,
    int ProductosEliminados, int VariantesNuevas, int VariantesActualizadas, int ExistenciasActualizadas,
    int CategoriasSinEstacion, string? Aviso);

public static class TiposSincronizacion
{
    public const string Catalogo = "Catálogo", Inventario = "Inventario";
    public const string EnProceso = "En proceso", Correcta = "Correcta", Fallida = "Fallida";
}

// ------------------------------------------------------------------ caso de uso

public sealed class SincronizarCatalogo(IOrigenCatalogo origen, ICatalogoRepository repositorio, TimeProvider reloj)
{
    /// <summary>Si la última sincronización completa es más vieja que esto, se vuelve a traer todo el catálogo.</summary>
    public static readonly TimeSpan MaximoSinSincronizacionCompleta = TimeSpan.FromHours(24);
    public const string PrefijoSinCategoria = "loyorder-sin-categoria-";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Una sola sincronización a la vez en todo el proceso (la programada y la manual comparten este candado).
    private static readonly SemaphoreSlim Candado = new(1, 1);

    public async Task<ResumenSincronizacion> EjecutarAsync(long? idRestaurante, CancellationToken ct)
    {
        if (!await Candado.WaitAsync(0, ct)) throw new SincronizacionEnCursoException();
        try { return await SincronizarAsync(idRestaurante, ct); }
        finally { Candado.Release(); }
    }

    private async Task<ResumenSincronizacion> SincronizarAsync(long? idRestaurante, CancellationToken ct)
    {
        var restaurante = await repositorio.RestauranteAsync(idRestaurante, ct)
            ?? throw new RestauranteNoConfiguradoException("No hay un restaurante registrado para sincronizar.");
        var tienda = await TiendaAsync(restaurante, ct);
        var inicio = reloj.GetUtcNow();

        // 1. Catálogo (CU-10 pasos 1 a 4). Primero se lee todo de Loyverse y después se guarda en una transacción:
        //    si Loyverse falla a la mitad, no queda nada guardado a medias (EX-02).
        var idCatalogo = await repositorio.IniciarRegistroAsync(restaurante.IdRestaurante, TiposSincronizacion.Catalogo, inicio, ct);
        ResultadoCatalogo catalogo;
        DateTimeOffset? desde;
        try
        {
            var ultima = await repositorio.UltimaCorrectaAsync(restaurante.IdRestaurante, TiposSincronizacion.Catalogo, ct);
            desde = ultima is { } u && inicio - u < MaximoSinSincronizacionCompleta ? u : null;   // FA 2.a
            var categorias = await origen.CategoriasAsync(ct);
            var articulos = await origen.ArticulosAsync(tienda, desde, ct);
            var (cats, productos) = Preparar(restaurante.IdRestaurante, categorias, articulos);
            catalogo = await repositorio.GuardarCatalogoAsync(restaurante.IdRestaurante, cats, productos, ct);
            await repositorio.TerminarRegistroAsync(idCatalogo, TiposSincronizacion.Correcta, reloj.GetUtcNow(), null,
                JsonSerializer.Serialize(new { completa = desde is null, catalogo }, Json), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await RegistrarFallaAsync(idCatalogo, e);
            throw;
        }

        // 2. Existencias de la tienda (CU-10 pasos 5 y 6).
        var idInventario = await repositorio.IniciarRegistroAsync(restaurante.IdRestaurante, TiposSincronizacion.Inventario, reloj.GetUtcNow(), ct);
        int existencias;
        try
        {
            var niveles = await origen.ExistenciasAsync(tienda, ct);
            existencias = await repositorio.GuardarExistenciasAsync(niveles, ct);
            await repositorio.TerminarRegistroAsync(idInventario, TiposSincronizacion.Correcta, reloj.GetUtcNow(), null,
                JsonSerializer.Serialize(new { existenciasActualizadas = existencias }, Json), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await RegistrarFallaAsync(idInventario, e);
            throw;
        }

        // 3. Aviso de FA 4.a: categorías sin estación (sus comandas salen en la estación general).
        var sinEstacion = await repositorio.CategoriasSinEstacionAsync(restaurante.IdRestaurante, ct);
        return new ResumenSincronizacion(restaurante.IdRestaurante, tienda, desde is null, inicio, reloj.GetUtcNow(),
            catalogo.CategoriasNuevas, catalogo.CategoriasActualizadas, catalogo.ProductosNuevos, catalogo.ProductosActualizados,
            catalogo.ProductosEliminados, catalogo.VariantesNuevas, catalogo.VariantesActualizadas, existencias, sinEstacion,
            sinEstacion > 0 ? "Hay categorías nuevas sin estación asignada" : null);
    }

    /// <summary>Tienda vinculada (CU-08 RN-03). Si no hay y la cuenta tiene una sola tienda, se vincula esa.</summary>
    private async Task<string> TiendaAsync(RestauranteSincronizable restaurante, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(restaurante.IdTiendaLoyverse)) return restaurante.IdTiendaLoyverse;
        var tiendas = await origen.TiendasAsync(ct);
        if (tiendas.Count != 1)
            throw new RestauranteNoConfiguradoException("La cuenta de Loyverse tiene varias tiendas o ninguna: elige la tienda en la configuración del restaurante.");
        await repositorio.VincularTiendaAsync(restaurante.IdRestaurante, tiendas[0].Id, ct);
        return tiendas[0].Id;
    }

    private async Task RegistrarFallaAsync(long idSincronizacion, Exception e)
    {
        // Solo mensajes propios: nunca se guarda el texto de la respuesta de Loyverse ni de PostgreSQL.
        var mensaje = e switch
        {
            OrigenNoAutorizadoException => "El token de Loyverse no es válido",
            OrigenNoConfiguradoException => "Falta configurar el token de Loyverse",
            OrigenNoDisponibleException => "No fue posible conectar con Loyverse",
            DependenciaNoDisponibleException => "No fue posible guardar en la base de datos",
            _ => "Error inesperado durante la sincronización"
        };
        try { await repositorio.TerminarRegistroAsync(idSincronizacion, TiposSincronizacion.Fallida, reloj.GetUtcNow(), mensaje, null, CancellationToken.None); }
        catch (DependenciaNoDisponibleException) { /* si la BD tampoco responde no hay dónde registrarlo */ }
    }

    /// <summary>Convierte lo que llega de Loyverse en filas de LoyOrder (CU-10 paso 3).</summary>
    public static (IReadOnlyList<CategoriaGuardar> Categorias, IReadOnlyList<ProductoGuardar> Productos) Preparar(
        long idRestaurante, IReadOnlyList<CategoriaOrigen> categorias, IReadOnlyList<ArticuloOrigen> articulos)
    {
        var lista = categorias.Select(c => new CategoriaGuardar(c.Id, Recortar(c.Nombre, 100)!, !c.Eliminada)).ToList();
        var conocidas = categorias.Where(c => !c.Eliminada).Select(c => c.Id).ToHashSet();
        var sinCategoria = PrefijoSinCategoria + idRestaurante;
        var productos = new List<ProductoGuardar>();
        foreach (var a in articulos)
        {
            // Los productos sin categoría (o con una categoría eliminada) se agrupan en "Sin categoría".
            var categoria = a.CategoriaId is { } c && conocidas.Contains(c) ? c : sinCategoria;
            if (categoria == sinCategoria && !a.Eliminado && lista.All(x => x.IdLoyverse != sinCategoria))
                lista.Add(new CategoriaGuardar(sinCategoria, "Sin categoría", true));
            var variantes = a.Variantes.Select(v => new VarianteGuardar(
                v.Id, Recortar(v.Nombre, 100),
                v.Precio ?? 0m,
                a.ControlaInventario,
                // Precio variable (null) o no disponible en la tienda: no se puede pedir en línea.
                !a.Eliminado && v.DisponibleEnTienda && v.Precio is >= 0)).ToList();
            productos.Add(new ProductoGuardar(a.Id, categoria, Recortar(a.Nombre, 150)!, LimpiadorHtml.Limpiar(a.Descripcion),
                a.UrlImagen, !a.Eliminado && variantes.Any(v => v.Disponible), a.Eliminado, variantes));
        }
        return (lista, productos);
    }

    private static string? Recortar(string? texto, int maximo)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        texto = texto.Trim();
        return texto.Length <= maximo ? texto : texto[..maximo];
    }
}

/// <summary>Quita el HTML de las descripciones que se editaron en el Back Office de Loyverse (prueba P06).</summary>
public static partial class LimpiadorHtml
{
    [GeneratedRegex(@"<\s*(br|/p|/div|/li)\s*/?\s*>", RegexOptions.IgnoreCase)] private static partial Regex Saltos();
    [GeneratedRegex(@"<[^>]*>")] private static partial Regex Etiquetas();
    [GeneratedRegex(@"[ \t ]+")] private static partial Regex Espacios();
    [GeneratedRegex(@"\s*\n\s*")] private static partial Regex Renglones();

    public static string? Limpiar(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var texto = Saltos().Replace(html, "\n");
        texto = Etiquetas().Replace(texto, "");
        texto = WebUtility.HtmlDecode(texto);
        texto = Espacios().Replace(texto, " ");
        texto = Renglones().Replace(texto, "\n").Trim();
        return texto.Length == 0 ? null : texto;
    }
}
