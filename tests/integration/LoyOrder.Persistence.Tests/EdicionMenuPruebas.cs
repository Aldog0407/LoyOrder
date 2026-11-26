using LoyOrder.Application;
using LoyOrder.Infrastructure.Persistence;
using Npgsql;

static class EdicionMenuPostgres
{
    public static async Task<int> ComprobarAsync(NpgsqlDataSource fuente)
    {
        await using var seed = fuente.CreateCommand("""
            WITH u AS (
                INSERT INTO usuario(nombre, correo, contrasena_hash)
                VALUES('Administrador del menú','menu-admin@prueba.test','solo-pruebas')
                RETURNING id_usuario
            )
            INSERT INTO empleado(id_empleado, id_restaurante, rol)
            SELECT id_usuario, 1, 'ADMINISTRADOR' FROM u
            RETURNING id_empleado
            """);
        var admin = (long)(await seed.ExecuteScalarAsync())!;
        var editor = new EditarMenu(new EdicionMenuRepository(fuente));
        var productos = await editor.ListarAsync(admin, default);
        Exigir(productos.All(p => p.NombreOrigen != "Tacos"), "El editor listó un producto del otro restaurante");
        await using var ajeno = fuente.CreateCommand("SELECT id_producto FROM producto WHERE id_loyverse = 'a-otro'");
        var idAjeno = (long)(await ajeno.ExecuteScalarAsync())!;
        try
        {
            await editor.GuardarAsync(admin, idAjeno, new("Ajeno", null, null), default);
            throw new Exception("El administrador pudo editar un producto del otro restaurante");
        }
        catch (ProductoMenuNoEncontradoException) { }

        var menu = new MenuRepository(fuente);
        var producto = productos.Single(p => p.NombreOrigen == "Alitas BBQ");
        var antes = (await menu.ListarAsync(1, default)).First(p => p.IdProducto == producto.Id);
        var cambios = new DatosMenuLocal("Alitas de la casa", "Salsa de la casa", "https://example.test/alitas.png");
        await editor.GuardarAsync(admin, producto.Id, cambios, default);
        var editado = (await menu.ListarAsync(1, default)).First(p => p.IdProducto == producto.Id);
        Exigir(editado.Producto == cambios.Nombre && editado.Descripcion == cambios.Descripcion &&
            editado.ImagenUrl == cambios.ImagenUrl && editado.Precio == antes.Precio &&
            editado.IdVariante == antes.IdVariante, "El menú no mostró la edición o cambió datos comerciales");

        var catalogo = new CatalogoRepository(fuente);
        await catalogo.GuardarCatalogoAsync(1, [new("c-alitas", "Alitas", true)], [
            new("a-alitas", "c-alitas", "Alitas Loyverse", "Descripción nueva", "https://example.test/origen.png",
                true, false, [new("v-6", "6 piezas", 120m, true, true)])
        ], default);
        var sincronizado = (await menu.ListarAsync(1, default)).First(p => p.IdProducto == producto.Id);
        Exigir(sincronizado.Precio == 120m && sincronizado.Producto == cambios.Nombre &&
            sincronizado.Descripcion == cambios.Descripcion && sincronizado.ImagenUrl == cambios.ImagenUrl,
            "La sincronización perdió cambios locales o dejó de actualizar el precio");
        await editor.GuardarAsync(admin, producto.Id, new(null, null, null), default);
        var restaurado = (await menu.ListarAsync(1, default)).First(p => p.IdProducto == producto.Id);
        Exigir(restaurado.Producto == "Alitas Loyverse" && restaurado.Descripcion == "Descripción nueva" &&
            restaurado.ImagenUrl == "https://example.test/origen.png", "No se restablecieron los datos de Loyverse");
        Console.WriteLine("  OK edición de menú: aislamiento por restaurante, publicación, sincronización y restablecimiento");
        return 1;
    }

    private static void Exigir(bool condicion, string mensaje)
    {
        if (!condicion) throw new Exception(mensaje);
    }
}
