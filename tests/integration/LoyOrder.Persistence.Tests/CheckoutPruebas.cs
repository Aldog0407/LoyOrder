using LoyOrder.Application;
using LoyOrder.Infrastructure.Persistence;
using Npgsql;

static class CheckoutPostgres
{
    public static async Task<int> ComprobarAsync(NpgsqlDataSource fuente, string raiz)
    {
        async Task Sql(string texto) { await using var c = fuente.CreateCommand(texto); await c.ExecuteNonQueryAsync(); }
        async Task<T> Valor<T>(string texto) { await using var c = fuente.CreateCommand(texto); return (T)(await c.ExecuteScalarAsync())!; }
        static void Exigir(bool valor, string mensaje) { if (!valor) throw new Exception(mensaje); }
        static async Task Esperar<T>(Func<Task> ejecutar) where T : Exception
        {
            try { await ejecutar(); } catch (T) { return; }
            throw new Exception("Se esperaba " + typeof(T).Name);
        }
        var total = 0;
        void Ok(string nombre) => Console.WriteLine($"  OK checkout {++total}. {nombre}");
        var restaurante = await Valor<long>("SELECT min(id_restaurante) FROM restaurante");
        await Sql($"UPDATE restaurante SET horario_apertura='00:00',horario_cierre='23:59',costo_envio=25 WHERE id_restaurante={restaurante}");
        var cliente = await Valor<long>("""
            WITH u AS (INSERT INTO usuario(nombre,correo,contrasena_hash) VALUES('Cliente checkout','checkout@prueba.test','solo-pruebas') RETURNING id_usuario)
            INSERT INTO cliente(id_cliente) SELECT id_usuario FROM u RETURNING id_cliente
            """);
        var otro = await Valor<long>("""
            WITH u AS (INSERT INTO usuario(nombre,correo,contrasena_hash) VALUES('Otro checkout','checkout-otro@prueba.test','solo-pruebas') RETURNING id_usuario)
            INSERT INTO cliente(id_cliente) SELECT id_usuario FROM u RETURNING id_cliente
            """);
        var pago = await Valor<long>($"INSERT INTO metodo_pago(id_restaurante,nombre,id_loyverse) VALUES({restaurante},'Efectivo checkout','p-checkout') RETURNING id_metodo_pago");
        var variante = await Valor<long>($"""
            WITH c AS (INSERT INTO categoria(id_restaurante,nombre) VALUES({restaurante},'Checkout') RETURNING id_categoria),
            p AS (INSERT INTO producto(id_categoria,nombre) SELECT id_categoria,'Producto checkout' FROM c RETURNING id_producto)
            INSERT INTO variante_producto(id_producto,nombre,id_loyverse,precio,controla_inventario)
            SELECT id_producto,'Normal','v-checkout',100,false FROM p RETURNING id_variante
            """);
        var origen = new Origen();
        var reloj = new Reloj { Ahora=new(2026,11,25,18,0,0,TimeSpan.Zero) };
        var servicio = new CrearPedido(new CrearPedidoRepository(fuente),origen,reloj,TimeZoneInfo.Utc);
        SolicitudPedido Nueva() => new(Guid.NewGuid(),"Recolección",pago,200,[new(variante,2,100)],null);
        var solicitud = Nueva();
        var simultaneos = await Task.WhenAll(servicio.EjecutarAsync(cliente,restaurante,solicitud,default),
            servicio.EjecutarAsync(cliente,restaurante,solicitud,default));
        Exigir(simultaneos[0].Folio == simultaneos[1].Folio,"Dos confirmaciones crearon folios diferentes");
        Exigir(await Valor<long>($"SELECT count(*) FROM pedido WHERE id_cliente={cliente} AND id_operacion='{solicitud.IdOperacion}'") == 1,"Pedido duplicado");
        Exigir(await Valor<long>($"SELECT count(*) FROM detalle_pedido WHERE id_pedido={simultaneos[0].IdPedido}") == 1,"Detalle duplicado");
        Exigir(await Valor<long>($"SELECT count(*) FROM historial_estado WHERE id_pedido={simultaneos[0].IdPedido}") == 1,"Historial duplicado");
        Exigir(simultaneos[0].Folio.Length <= 30,"Folio fuera del esquema");
        await Esperar<CheckoutCambioException>(() => servicio.EjecutarAsync(cliente,restaurante,
            solicitud with { TotalEsperado=100,Articulos=[new(variante,1,100)] },default));
        Ok("confirmación concurrente, folio e historial únicos y rechazo de otra solicitud con la misma operación");

        var repositorio = new PedidoClienteRepository(fuente);
        var pedido = await repositorio.ConsultarAsync(cliente,simultaneos[0].Folio,default);
        Exigir(pedido?.Articulos.Single().IdVariante == variante,"No se conservó la variante");
        Exigir(await repositorio.ConsultarAsync(otro,simultaneos[0].Folio,default) is null,"Pedido expuesto a otro cliente");
        var historial = new HistorialPedidoRepository(fuente);
        Exigir((await historial.ListarAsync(cliente,1,20,default)).Count == 1,"Falta el pedido en el historial persistido");
        Exigir((await historial.ListarAsync(otro,1,20,default)).Count == 0,"El historial mezcla clientes");
        Ok("consulta, variantes para repetición e historial por propietario");

        var domicilio = Nueva() with { TipoEntrega="Domicilio",TotalEsperado=225,
            Direccion=new("Calle prueba","123","Referencia de prueba",19.5438m,-96.9271m) };
        var entregable = await servicio.EjecutarAsync(cliente,restaurante,domicilio,default);
        Exigir(entregable.Subtotal == 200 && entregable.CostoEnvio == 25 && entregable.Total == 225,"Costo incorrecto");
        Exigir(await Valor<long>($"SELECT count(*) FROM direccion d JOIN pedido p ON p.id_direccion=d.id_direccion WHERE p.id_pedido={entregable.IdPedido} AND d.id_cliente={cliente}") == 1,"Dirección sin propietario");
        await Esperar<CheckoutCambioException>(() => servicio.EjecutarAsync(cliente,restaurante,Nueva() with { TotalEsperado=1 },default));
        await Sql($"UPDATE variante_producto SET precio=101 WHERE id_variante={variante}");
        await Esperar<CheckoutCambioException>(() => servicio.EjecutarAsync(cliente,restaurante,Nueva(),default));
        await Sql($"UPDATE variante_producto SET precio=100,controla_inventario=true WHERE id_variante={variante}");
        origen.Existencias.Add(new("v-checkout",1));
        await Esperar<CheckoutCambioException>(() => servicio.EjecutarAsync(cliente,restaurante,Nueva(),default));
        await Sql($"UPDATE variante_producto SET controla_inventario=false WHERE id_variante={variante}");
        Ok("domicilio, costo del servidor, precio manipulado y stock insuficiente");

        await Sql("""
            CREATE FUNCTION public.falla_checkout_prueba() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.nombre_producto='Producto checkout' THEN RAISE EXCEPTION 'Falla de prueba'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER falla_checkout_prueba BEFORE INSERT ON detalle_pedido FOR EACH ROW EXECUTE FUNCTION public.falla_checkout_prueba();
            """);
        var fallida = domicilio with { IdOperacion=Guid.NewGuid() };
        var direccionesAntes = await Valor<long>($"SELECT count(*) FROM direccion WHERE id_cliente={cliente}");
        try { await Esperar<DependenciaNoDisponibleException>(() => servicio.EjecutarAsync(cliente,restaurante,fallida,default)); }
        finally { await Sql("DROP TRIGGER falla_checkout_prueba ON detalle_pedido; DROP FUNCTION public.falla_checkout_prueba()"); }
        Exigir(await Valor<long>($"SELECT count(*) FROM pedido WHERE id_operacion='{fallida.IdOperacion}'") == 0,"Quedó un pedido incompleto");
        Exigir(await Valor<long>($"SELECT count(*) FROM direccion WHERE id_cliente={cliente}") == direccionesAntes,"Quedó una dirección huérfana");
        await servicio.EjecutarAsync(cliente,restaurante,fallida,default);
        Ok("fallo parcial revierte pedido y dirección, y permite reintentar");

        await Sql($"UPDATE restaurante SET horario_apertura='09:00',horario_cierre='10:00' WHERE id_restaurante={restaurante}");
        var repetido = await servicio.EjecutarAsync(cliente,restaurante,solicitud,default);
        Exigir(repetido.Folio == simultaneos[0].Folio,"El horario bloqueó recuperar un pedido ya confirmado");
        await Esperar<CheckoutCambioException>(() => servicio.EjecutarAsync(cliente,restaurante,Nueva(),default));
        await Sql($"INSERT INTO pedido(id_pedido,folio,id_cliente,id_metodo_pago,tipo_entrega,subtotal,costo_envio,total) VALUES(900000,'LEGACY-CHECKOUT',{cliente},{pago},'Recolección',0,0,0)");
        await Sql(File.ReadAllText(Path.Combine(raiz,"database/scripts/LoyOrderCheckout.sql")));
        await Sql(File.ReadAllText(Path.Combine(raiz,"database/scripts/LoyOrderCheckout.sql")));
        Exigir(await Valor<long>($"SELECT count(*) FROM pedido WHERE id_operacion='{solicitud.IdOperacion}'") == 1,"La migración alteró pedidos");
        Exigir(await Valor<long>("SELECT count(*) FROM pedido WHERE folio='LEGACY-CHECKOUT' AND id_operacion IS NULL") == 1,"La migración alteró el pedido anterior");
        await Sql($"UPDATE restaurante SET horario_apertura='00:00',horario_cierre='23:59' WHERE id_restaurante={restaurante}");
        var posterior = await servicio.EjecutarAsync(cliente,restaurante,Nueva(),default);
        Exigir(posterior.IdPedido > 900000,"La secuencia no contempla IDs explícitos anteriores");
        Ok("reintento tras cierre, migración repetible y compatibilidad con pedidos anteriores");
        await Sql($"UPDATE usuario SET correo_verificado_en=clock_timestamp() WHERE id_usuario={cliente}");
        var operador = await Valor<long>($"""
            WITH u AS (INSERT INTO usuario(nombre,correo,contrasena_hash) VALUES('Operador checkout','operador-checkout@prueba.test','solo-pruebas') RETURNING id_usuario)
            INSERT INTO empleado(id_empleado,id_restaurante,rol) SELECT id_usuario,{restaurante},'OPERADOR' FROM u RETURNING id_empleado
            """);
        var restauranteAjeno = await Valor<long>("INSERT INTO restaurante(nombre,horario_apertura,horario_cierre,costo_envio) VALUES('Ajeno','00:00','23:59',0) RETURNING id_restaurante");
        var operadorAjeno = await Valor<long>($"""
            WITH u AS (INSERT INTO usuario(nombre,correo,contrasena_hash) VALUES('Operador ajeno','operador-ajeno@prueba.test','solo-pruebas') RETURNING id_usuario)
            INSERT INTO empleado(id_empleado,id_restaurante,rol) SELECT id_usuario,{restauranteAjeno},'OPERADOR' FROM u RETURNING id_empleado
            """);
        var operacion = new OperacionPedidoRepository(fuente);
        var folio = simultaneos[0].Folio;
        Exigir((await operacion.ListarAsync(operador,default)).Any(p => p.Folio==folio),"Falta el pedido del restaurante");
        Exigir((await operacion.ListarAsync(operadorAjeno,default)).Count==0,"El operador ve otro restaurante");
        await Esperar<PedidoNoEncontradoException>(() => operacion.CambiarAsync(operadorAjeno,folio,"En preparación",null,default));
        await Esperar<CheckoutCambioException>(() => operacion.CambiarAsync(operador,folio,"Entregado",null,default));
        await operacion.CambiarAsync(operador,folio,"En preparación",null,default);
        await Esperar<CheckoutCambioException>(() => operacion.CambiarAsync(operador,folio,"Enviado",null,default));
        await operacion.CambiarAsync(operador,folio,"Listo para recoger",null,default);
        await operacion.CambiarAsync(operador,folio,"Entregado",null,default);
        await operacion.CambiarAsync(operador,folio,"Entregado",null,default);
        Exigir(await Valor<long>($"SELECT count(*) FROM historial_estado WHERE id_pedido={simultaneos[0].IdPedido}")==4,"Un reintento duplicó el evento");
        Exigir(await Valor<long>($"SELECT count(*) FROM notificacion WHERE id_pedido={simultaneos[0].IdPedido}")==4,"No hay un aviso por evento");
        Exigir(await Valor<long>($"SELECT count(*) FROM registro_venta WHERE id_pedido={simultaneos[0].IdPedido} AND estado_sincronizacion='Pendiente'")==1,"El cierre no dejó pendiente la venta");
        await repositorio.CalificarAsync(cliente,folio,5,"Entregado",default);
        Exigir((await repositorio.ConsultarAsync(cliente,folio,default))?.Calificacion==5,"No permite calificar la entrega");
        Ok("operador por restaurante, transiciones, reintentos y cierre habilitan la calificación");

        var avisos = new NotificacionPedidoRepository(fuente);
        var recibidos = new System.Collections.Concurrent.ConcurrentQueue<MensajeCorreo>();
        async Task Enviar(MensajeCorreo m,CancellationToken ct) { recibidos.Enqueue(m); await Task.Delay(50,ct); }
        await Task.WhenAll(avisos.ProcesarUnaAsync(Enviar,default),avisos.ProcesarUnaAsync(Enviar,default));
        Exigir(recibidos.Select(m => m.IdMensaje).Distinct().Count()==recibidos.Count,"Dos trabajadores enviaron el mismo aviso");
        while (await avisos.ProcesarUnaAsync(Enviar,default)) { }
        Exigir(recibidos.All(m => m.Destino=="checkout@prueba.test"),"Aviso al destinatario incorrecto");
        Ok("avisos persistidos y trabajadores simultáneos sin reclamar la misma fila");

        var paraFallo = await servicio.EjecutarAsync(cliente,restaurante,Nueva(),default);
        var idAviso = await Valor<long>($"SELECT id_notificacion FROM notificacion WHERE id_pedido={paraFallo.IdPedido}");
        string? identificador = null;
        Task Fallar(MensajeCorreo m,CancellationToken ct) { identificador=m.IdMensaje; throw new IOException("Falla SMTP de prueba"); }
        await avisos.ProcesarUnaAsync(Fallar,default);
        Exigir(await Valor<string>($"SELECT estado_envio FROM notificacion WHERE id_notificacion={idAviso}")=="Fallida","Falta registrar falla SMTP");
        Exigir(!await avisos.ProcesarUnaAsync(Enviar,default),"Se ignoró la espera del reintento");
        Exigir(await Valor<long>($"SELECT count(*) FROM pedido WHERE id_pedido={paraFallo.IdPedido}")==1,"SMTP revirtió el pedido");
        await Sql($"UPDATE notificacion SET proximo_intento=clock_timestamp()-interval '1 second' WHERE id_notificacion={idAviso}");
        await new NotificacionPedidoRepository(fuente).ProcesarUnaAsync(Enviar,default);
        Exigir(recibidos.Last().IdMensaje==identificador,"El reintento cambió el Message-ID");
        Exigir(await Valor<short>($"SELECT intentos FROM notificacion WHERE id_notificacion={idAviso}")==2,"Falta el historial de intentos");
        Ok("SMTP fallido conserva el pedido y un procesador nuevo retoma el aviso");

        var agotable = await servicio.EjecutarAsync(cliente,restaurante,Nueva(),default);
        for (var intento=0;intento<5;intento++)
        {
            await Sql($"UPDATE notificacion SET proximo_intento=clock_timestamp()-interval '1 second' WHERE id_pedido={agotable.IdPedido}");
            await avisos.ProcesarUnaAsync(Fallar,default);
        }
        Exigir(!await avisos.ProcesarUnaAsync(Enviar,default),"Se superó el máximo de cinco intentos");
        await Esperar<SolicitudInvalidaException>(() => new OperacionPedidos(operacion).CambiarAsync(operador,agotable.Folio,"Cancelado",null,default));
        await Task.WhenAll(operacion.CambiarAsync(operador,agotable.Folio,"Cancelado","Sin ingrediente",default),
            operacion.CambiarAsync(operador,agotable.Folio,"Cancelado","Sin ingrediente",default));
        Exigir(await Valor<long>($"SELECT count(*) FROM historial_estado WHERE id_pedido={agotable.IdPedido}")==2,"Se duplicó una cancelación concurrente");
        Exigir(await Valor<long>($"SELECT count(*) FROM notificacion WHERE id_pedido={agotable.IdPedido}")==2,"Se duplicó el aviso de cancelación");
        Exigir(await Valor<long>($"SELECT count(*) FROM notificacion WHERE id_pedido={agotable.IdPedido} AND mensaje LIKE '%Motivo: Sin ingrediente%'")==1,"El aviso de cancelación omite el motivo");
        Ok("máximo de cinco intentos y cancelación concurrente con motivo obligatorio");
        return total;
    }
}
