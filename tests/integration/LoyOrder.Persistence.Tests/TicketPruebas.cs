using LoyOrder.Application;
using LoyOrder.Infrastructure.Persistence;
using Npgsql;

// 7.2.11 · CU-19 ticket de venta con PostgreSQL real: datos del pedido, alcance por restaurante del operador,
// impresora de caja activa y restaurante del administrador.
static class TicketPostgres
{
    public static async Task<int> ComprobarAsync(NpgsqlDataSource fuente)
    {
        await Ejecutar(fuente, """
            WITH cu AS (INSERT INTO usuario(nombre, correo, contrasena_hash) VALUES ('María Ticket', 'ticket-cliente@prueba.test', 'solo-pruebas') RETURNING id_usuario),
                 c AS (INSERT INTO cliente(id_cliente, telefono) SELECT id_usuario, '2281234567' FROM cu RETURNING id_cliente),
                 d AS (INSERT INTO direccion(id_cliente, calle, numero, colonia, referencias)
                       SELECT id_cliente, 'Av. Juárez', '12', 'Centro', 'Portón verde' FROM c RETURNING id_direccion, id_cliente),
                 mp AS (INSERT INTO metodo_pago(id_restaurante, nombre, id_loyverse) VALUES (1, 'Efectivo', 'mp-ticket') RETURNING id_metodo_pago),
                 p AS (INSERT INTO pedido(folio, id_cliente, id_direccion, id_metodo_pago, estado, tipo_entrega, subtotal, costo_envio, total)
                       SELECT 'TICKET-1', d.id_cliente, d.id_direccion, mp.id_metodo_pago, 'En preparación', 'Domicilio', 190, 25, 215 FROM d, mp
                       RETURNING id_pedido)
            INSERT INTO detalle_pedido(id_pedido, id_variante, nombre_producto, cantidad, precio_unitario, subtotal, indicaciones)
            SELECT p.id_pedido, v.id_variante, 'Alitas BBQ', 2, 95, 190, 'Salsa extra' FROM p, variante_producto v WHERE v.id_loyverse = 'v-6';
            INSERT INTO impresora(id_restaurante, nombre, direccion_ip, tipo, activa) VALUES
                (1, 'Caja vieja', '10.0.0.1', 'Caja', false), (1, 'Cocina alitas', '10.0.0.2', 'Cocina', true),
                (1, 'Caja', '192.168.123.100', 'Caja', true);
            """);
        var operador = await Empleado(fuente, "ticket-op@prueba.test", 1, "OPERADOR");
        var operadorOtro = await Empleado(fuente, "ticket-op-otro@prueba.test", 2, "OPERADOR");
        var administrador = await Empleado(fuente, "ticket-admin@prueba.test", 1, "ADMINISTRADOR");
        var repo = new TicketRepository(fuente);

        var t = await repo.ObtenerAsync(operador, "TICKET-1", default) ?? throw new Exception("El operador no encontró su pedido");
        Exigir(t is { IdRestaurante: 1, Restaurante: "Prueba", Cliente: "María Ticket", TelefonoCliente: "2281234567", Calle: "Av. Juárez",
            Numero: "12", Colonia: "Centro", Referencias: "Portón verde", MetodoPago: "Efectivo", TipoEntrega: "Domicilio", Total: 215m }, "datos del pedido");
        Exigir(t.Articulos.Single() is { Nombre: "Alitas BBQ", Cantidad: 2, Importe: 190m, Indicaciones: "Salsa extra" } a && a.Variante is not null, "artículo con su variante");
        Exigir(await repo.ObtenerAsync(operadorOtro, "TICKET-1", default) is null, "un operador de otro restaurante no ve el pedido");
        Exigir(await repo.ObtenerAsync(administrador, "TICKET-1", default) is null, "el administrador no imprime tickets (matriz 4.2)");
        Exigir(await repo.ObtenerAsync(operador, "NO-EXISTE", default) is null, "folio inexistente");

        var caja = await repo.ImpresoraCajaAsync(1, default);
        Exigir(caja is { Nombre: "Caja", Direccion: "192.168.123.100" }, "solo la impresora de caja activa");
        Exigir(await repo.ImpresoraCajaAsync(2, default) is null, "restaurante sin impresora de caja");
        Exigir(await repo.RestauranteDeAdministradorAsync(administrador, default) == (1L, "Prueba"), "restaurante del administrador");
        Exigir(await repo.RestauranteDeAdministradorAsync(operador, default) is null, "un operador no es administrador");

        var servicio = new ServicioTicket(repo, new SinImpresora(), new ConfiguracionTicket(null, null, TimeZoneInfo.Utc), TimeProvider.System);
        var texto = (await servicio.VistaPreviaAsync(operador, "TICKET-1", false, default)).ComoTexto();
        Exigir(texto.Contains("2 x Alitas BBQ") && texto.Contains("    > Salsa extra") && texto.Contains("TOTAL $215.00")
            && texto.Contains("Av. Juárez 12, Col. Centro"), "vista previa con los datos reales");

        await Ejecutar(fuente, $"UPDATE usuario SET activo = false WHERE id_usuario = {operador}");
        Exigir(await repo.ObtenerAsync(operador, "TICKET-1", default) is null, "un operador inactivo no ve el pedido");
        Console.WriteLine("  OK   ticket de venta: datos, alcance por restaurante e impresora de caja");
        return 1;
    }

    private static async Task<long> Empleado(NpgsqlDataSource fuente, string correo, long restaurante, string rol)
    {
        await using var cmd = fuente.CreateCommand("""
            WITH u AS (INSERT INTO usuario(nombre, correo, contrasena_hash) VALUES ($1, $1, 'solo-pruebas') RETURNING id_usuario)
            INSERT INTO empleado(id_empleado, id_restaurante, rol) SELECT id_usuario, $2, $3 FROM u RETURNING id_empleado
            """);
        cmd.Parameters.AddWithValue(correo);
        cmd.Parameters.AddWithValue(restaurante);
        cmd.Parameters.AddWithValue(rol);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task Ejecutar(NpgsqlDataSource fuente, string sql)
    {
        await using var cmd = fuente.CreateCommand(sql);
        await cmd.ExecuteNonQueryAsync();
    }

    private static void Exigir(bool condicion, string que) { if (!condicion) throw new Exception($"Ticket: {que}"); }

    private sealed class SinImpresora : IImpresoraTermica
    {
        public Task ImprimirAsync(ImpresoraDestino destino, DocumentoImpresion documento, CancellationToken ct) => Task.CompletedTask;
        public Task ImprimirPruebaAsync(ImpresoraDestino destino, DocumentoImpresion encabezado, CancellationToken ct) => Task.CompletedTask;
    }
}
