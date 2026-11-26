using LoyOrder.Application;
using LoyOrder.Infrastructure.Persistence;
using Npgsql;

static class VentasPostgres
{
    public static async Task<int> ComprobarAsync(NpgsqlDataSource fuente)
    {
        async Task Sql(string s) { await using var c=fuente.CreateCommand(s);await c.ExecuteNonQueryAsync(); }
        async Task<T> Valor<T>(string s) { await using var c=fuente.CreateCommand(s);return (T)(await c.ExecuteScalarAsync())!; }
        static void Exigir(bool c,string m) { if(!c) throw new Exception(m); }
        var repo=new VentaRepository(fuente);
        Exigir(await repo.DisponibleAsync(default),"Falta migración de ventas");
        var operador=await Valor<long>("SELECT e.id_empleado FROM empleado e JOIN metodo_pago m ON m.id_restaurante=e.id_restaurante JOIN pedido p ON p.id_metodo_pago=m.id_metodo_pago JOIN registro_venta rv ON rv.id_pedido=p.id_pedido WHERE e.rol='OPERADOR' ORDER BY rv.id_registro LIMIT 1");
        var folio=await Valor<string>("SELECT p.folio FROM registro_venta rv JOIN pedido p ON p.id_pedido=rv.id_pedido ORDER BY rv.id_registro LIMIT 1");
        try { await repo.ReintentarAsync(-1,folio,default);throw new Exception("Un operador ajeno reintentó una venta"); } catch(PedidoNoEncontradoException) { }
        var tomados=await Task.WhenAll(repo.TomarAsync(default),repo.TomarAsync(default));
        var trabajos=tomados.Where(t=>t is not null).ToList();
        Exigir(trabajos.Count==1,"Se reclamó dos veces el mismo registro");
        var primero=trabajos[0]!;
        try { await repo.ReintentarAsync(operador,folio,default);throw new Exception("Se reinició una venta activa"); } catch(CheckoutCambioException) { }
        Exigir(!primero.SoloVerificar,"Un pendiente no es incierto");
        Exigir(primero.Datos.Lineas.Count>0,"Faltan artículos históricos");
        Exigir(await repo.TomarAsync(default) is null,"Se reclamó un trabajo activo");
        Console.WriteLine("  OK venta 1. reclamación concurrente y snapshot de pedido entregado");
        await Sql($"UPDATE registro_venta SET iniciado_en=clock_timestamp()-interval '4 minutes' WHERE id_registro={primero.IdRegistro}");
        var recuperado=(await new VentaRepository(fuente).TomarAsync(default))!;
        Exigir(recuperado.SoloVerificar,"Un proceso interrumpido puede volver a enviar");
        Exigir(recuperado.Proceso!=primero.Proceso,"Se reutilizó el dueño del trabajo");
        await repo.FinalizarAsync(primero,new("Confirmado","RECIBO-ANTIGUO",null),default);
        Exigir(await Valor<string>($"SELECT estado_sincronizacion FROM registro_venta WHERE id_registro={primero.IdRegistro}")=="En proceso","Un trabajador antiguo sobrescribió el resultado");
        await repo.FinalizarAsync(recuperado,new("Por verificar",null,"Respuesta incierta"),default);
        Exigir(await repo.TomarAsync(default) is null,"Se ignoró la espera");
        await repo.ReintentarAsync(operador,folio,default);
        var verificado=(await repo.TomarAsync(default))!;
        Exigir(verificado.SoloVerificar,"La incertidumbre se perdió al reintentar");
        await repo.FinalizarAsync(verificado,new("Confirmado","RECIBO-PG-PRUEBA",null),default);
        await repo.ReintentarAsync(operador,folio,default);
        Exigir(await repo.TomarAsync(default) is null,"Se volvió a procesar un confirmado");
        Exigir(await Valor<long>("SELECT count(*) FROM registro_venta WHERE numero_recibo='RECIBO-PG-PRUEBA'")==1,"Falta guardar el número");
        Exigir(await Valor<string>($"SELECT estado FROM pedido WHERE id_pedido={primero.Datos.IdPedido}")=="Entregado","Se alteró el estado del pedido");
        Console.WriteLine("  OK venta 2. recuperación tras interrupción, dueño del trabajo y confirmación persistida");
        return 2;
    }
}
