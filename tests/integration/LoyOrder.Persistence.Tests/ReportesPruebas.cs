using LoyOrder.Application;
using LoyOrder.Infrastructure.Persistence;
using Npgsql;

static class ReportesPostgres
{
    public static async Task<int> ComprobarAsync(NpgsqlDataSource fuente)
    {
        await using var datos = fuente.CreateCommand("""
            WITH restaurantes AS (
                INSERT INTO restaurante(nombre,horario_apertura,horario_cierre,costo_envio)
                VALUES ('Reportes propio','08:00','22:00',0), ('Reportes ajeno','08:00','22:00',0)
                RETURNING id_restaurante,nombre
            ), pagos AS (
                INSERT INTO metodo_pago(id_restaurante,nombre,id_loyverse)
                SELECT id_restaurante,'Efectivo',nombre FROM restaurantes
                RETURNING id_metodo_pago,id_loyverse
            ), usuarios AS (
                INSERT INTO usuario(nombre,correo,contrasena_hash)
                VALUES ('Admin Reportes','reportes-admin@prueba.test','solo-pruebas'),
                       ('Cliente Reportes','reportes-cliente@prueba.test','solo-pruebas')
                RETURNING id_usuario,correo
            ), cliente AS (
                INSERT INTO cliente(id_cliente)
                SELECT id_usuario FROM usuarios WHERE correo='reportes-cliente@prueba.test'
                RETURNING id_cliente
            ), pedidos AS (
                INSERT INTO pedido(folio,id_cliente,id_metodo_pago,fecha_creacion,fecha_entrega,estado,tipo_entrega,subtotal,costo_envio,total)
                SELECT filas.folio,c.id_cliente,m.id_metodo_pago,filas.creado,filas.entregado,filas.estado,'Recolección',50,0,50
                FROM cliente c CROSS JOIN pagos m JOIN (VALUES
                    ('REPORTES-ENTREGADO','Reportes propio',TIMESTAMPTZ '2032-11-11 20:00+00',TIMESTAMPTZ '2032-11-12 06:00+00','Entregado'),
                    ('REPORTES-RECIBIDO','Reportes propio',TIMESTAMPTZ '2032-11-13 05:59+00',NULL,'Recibido'),
                    ('REPORTES-FUERA','Reportes propio',TIMESTAMPTZ '2032-11-13 06:00+00',NULL,'Recibido'),
                    ('REPORTES-AJENO','Reportes ajeno',TIMESTAMPTZ '2032-11-12 15:00+00',TIMESTAMPTZ '2032-11-12 16:00+00','Entregado')
                ) AS filas(folio,tienda,creado,entregado,estado) ON filas.tienda=m.id_loyverse
            )
            INSERT INTO empleado(id_empleado,id_restaurante,rol)
            SELECT u.id_usuario,r.id_restaurante,'ADMINISTRADOR'
            FROM usuarios u CROSS JOIN restaurantes r
            WHERE u.correo='reportes-admin@prueba.test' AND r.nombre='Reportes propio'
            RETURNING id_empleado
            """);
        var admin = (long)(await datos.ExecuteScalarAsync())!;
        var reportes = new ConsultarReportes(new ReporteRepository(fuente), TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City"));
        var dia = new DateOnly(2032, 11, 12);

        var ventas = await reportes.EjecutarAsync(admin, "ventas", dia, dia, default);
        var venta = ventas.Registros.Single();
        if (venta.Folio != "REPORTES-ENTREGADO" || venta.Estado != "Sin registro" || venta.Fecha.Hour != 0 || ventas.Importe != 50m)
            throw new Exception("El reporte de ventas no filtró por entrega local o restaurante.");

        var pedidos = await reportes.EjecutarAsync(admin, "pedidos", dia, dia, default);
        if (pedidos.Registros.Single().Folio != "REPORTES-RECIBIDO" ||
            pedidos.Estados.Single() != new EstadoReporte("Recibido", 1, 50m))
            throw new Exception("El reporte de pedidos incluyó registros fuera de la fecha local o del restaurante.");

        Console.WriteLine("  OK reportes: fechas locales, aislamiento por restaurante e importes por estado");
        return 1;
    }
}
