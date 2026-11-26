using LoyOrder.Application;

static partial class Pruebas
{
    public static async Task Prueba_EdicionMenuNormalizaYRestableceCamposIndependientes()
    {
        var repo = new RepositorioEdicionMenuPrueba();
        await new EditarMenu(repo).GuardarAsync(12, 7,
            new("  Tacos de la casa ", "  ", " https://example.test/tacos.png "), default);
        if (repo.Cambio != (12, 7, new DatosMenuLocal("Tacos de la casa", null, "https://example.test/tacos.png")))
            throw new Exception("No se conservaron la identidad y los datos normalizados");
    }

    public static async Task Prueba_EdicionMenuRechazaDatosInvalidosAntesDeGuardar()
    {
        var repo = new RepositorioEdicionMenuPrueba();
        var menu = new EditarMenu(repo);
        foreach (var datos in new DatosMenuLocal[] {
            new(new string('x', 151), null, null),
            new(null, new string('x', 2001), null),
            new(null, null, "javascript:alert(1)"),
            new(null, null, "https://usuario:clave@example.test/imagen.png"),
            new(null, null, "https://example.test/imagen con espacios.png")
        })
        {
            try { await menu.GuardarAsync(12, 7, datos, default); }
            catch (SolicitudInvalidaException) { continue; }
            throw new Exception("Se aceptó un campo inválido");
        }
        if (repo.Cambio is not null) throw new Exception("La validación ocurrió después de guardar");
    }

    public static async Task Prueba_EdicionMenuInformaCuandoNoPuedeEditarElProducto()
    {
        var repo = new RepositorioEdicionMenuPrueba { Disponible = false };
        try { await new EditarMenu(repo).GuardarAsync(12, 7, new("Tacos", null, null), default); }
        catch (ProductoMenuNoEncontradoException) { return; }
        throw new Exception("No se informó el producto inaccesible");
    }

    private sealed class RepositorioEdicionMenuPrueba : IEdicionMenuRepository
    {
        public (long Administrador, long Producto, DatosMenuLocal Datos)? Cambio { get; private set; }
        public bool Disponible { get; init; } = true;

        public Task<IReadOnlyList<ProductoEditable>> ListarAsync(long administrador, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ProductoEditable>>([]);

        public Task<bool> GuardarAsync(long administrador, long producto, DatosMenuLocal datos, CancellationToken ct)
        {
            Cambio = (administrador, producto, datos);
            return Task.FromResult(Disponible);
        }
    }
}
