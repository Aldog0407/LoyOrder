namespace LoyOrder.Application;

public sealed record DatosMenuLocal(string? Nombre, string? Descripcion, string? ImagenUrl);
public sealed record ProductoEditable(long Id, string Categoria, string NombreOrigen, string? DescripcionOrigen,
    string? ImagenOrigen, DatosMenuLocal DatosLocales);

public sealed class ProductoMenuNoEncontradoException : Exception;

public interface IEdicionMenuRepository
{
    Task<IReadOnlyList<ProductoEditable>> ListarAsync(long administrador, CancellationToken ct);
    Task<bool> GuardarAsync(long administrador, long producto, DatosMenuLocal datos, CancellationToken ct);
}

public sealed class EditarMenu(IEdicionMenuRepository repo)
{
    public Task<IReadOnlyList<ProductoEditable>> ListarAsync(long administrador, CancellationToken ct) =>
        repo.ListarAsync(administrador, ct);

    public async Task GuardarAsync(long administrador, long producto, DatosMenuLocal datos, CancellationToken ct)
    {
        if (producto <= 0) throw new SolicitudInvalidaException("Selecciona un producto válido.");

        var nombre = Normalizar(datos.Nombre, 150, "nombre");
        var descripcion = Normalizar(datos.Descripcion, 2000, "descripción");
        var imagen = Normalizar(datos.ImagenUrl, 2048, "URL de imagen");
        if (imagen is not null &&
            (!Uri.TryCreate(imagen, UriKind.Absolute, out var uri) ||
             uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host) ||
             uri.UserInfo.Length != 0 || imagen.Any(char.IsWhiteSpace) || imagen.Contains('\\')))
            throw new SolicitudInvalidaException("Indica una URL de imagen HTTP o HTTPS válida.");

        if (!await repo.GuardarAsync(administrador, producto, new(nombre, descripcion, imagen), ct))
            throw new ProductoMenuNoEncontradoException();
    }

    private static string? Normalizar(string? valor, int maximo, string campo)
    {
        var texto = valor?.Trim();
        if (string.IsNullOrEmpty(texto)) return null;
        if (texto.Length > maximo || texto.Contains('\0'))
            throw new SolicitudInvalidaException($"Revisa el campo {campo}. Admite hasta {maximo} caracteres.");
        return texto;
    }
}
