-- LoyOrder | Actividad 7.1.6 | PostgreSQL 18
-- Diseño: vistas y operaciones para API de tres capas; sin insertar datos ficticios.
BEGIN;

CREATE OR REPLACE VIEW public.vw_pedidos_resumen AS
SELECT p.id_pedido, p.folio, p.fecha_creacion, p.fecha_entrega,
       p.estado, p.tipo_entrega, p.subtotal, p.costo_envio, p.total,
       u.nombre AS cliente, u.correo AS correo_cliente,
       mp.nombre AS metodo_pago,
       rv.estado_sincronizacion AS estado_venta_loyverse,
       rv.numero_recibo
FROM public.pedido p
JOIN public.cliente c ON c.id_cliente = p.id_cliente
JOIN public.usuario u ON u.id_usuario = c.id_cliente
JOIN public.metodo_pago mp ON mp.id_metodo_pago = p.id_metodo_pago
LEFT JOIN public.registro_venta rv ON rv.id_pedido = p.id_pedido;

CREATE OR REPLACE VIEW public.vw_detalle_pedidos AS
SELECT d.id_detalle, d.id_pedido, p.folio, p.estado, d.id_variante,
       d.nombre_producto, d.cantidad, d.precio_unitario, d.subtotal,
       d.indicaciones, vp.nombre AS variante,
       pr.nombre AS producto, cat.nombre AS categoria
FROM public.detalle_pedido d
JOIN public.pedido p ON p.id_pedido = d.id_pedido
JOIN public.variante_producto vp ON vp.id_variante = d.id_variante
JOIN public.producto pr ON pr.id_producto = vp.id_producto
JOIN public.categoria cat ON cat.id_categoria = pr.id_categoria;

CREATE OR REPLACE VIEW public.vw_estaciones_impresoras AS
SELECT e.id_estacion, e.nombre AS estacion, e.activa AS estacion_activa,
       e.id_restaurante, i.id_impresora, i.nombre AS impresora,
       i.direccion_ip, i.tipo AS tipo_impresora, i.activa AS impresora_activa
FROM public.estacion_preparacion e
JOIN public.impresora i ON i.id_impresora = e.id_impresora;

CREATE OR REPLACE VIEW public.vw_ventas_entregadas AS
SELECT p.id_pedido, p.folio, p.fecha_entrega, p.total,
       p.tipo_entrega, rv.estado_sincronizacion,
       rv.id_recibo_loyverse, rv.numero_recibo, rv.fecha_registro
FROM public.pedido p
LEFT JOIN public.registro_venta rv ON rv.id_pedido = p.id_pedido
WHERE p.estado = 'Entregado';

CREATE OR REPLACE FUNCTION public.fn_pedido_es_modificable(p_estado TEXT)
RETURNS BOOLEAN
LANGUAGE sql IMMUTABLE PARALLEL SAFE
AS $$ SELECT p_estado IN ('Recibido', 'En preparación') $$;

CREATE OR REPLACE FUNCTION public.fn_subtotal_detalles(p_id_pedido BIGINT)
RETURNS NUMERIC(12,2)
LANGUAGE sql STABLE
AS $$
    SELECT COALESCE(SUM(d.subtotal), 0)::NUMERIC(12,2)
    FROM public.detalle_pedido d
    WHERE d.id_pedido = p_id_pedido;
$$;

CREATE OR REPLACE FUNCTION public.fn_resumen_pedidos_por_estado()
RETURNS TABLE (estado VARCHAR(25), cantidad BIGINT, importe_total NUMERIC)
LANGUAGE sql STABLE
AS $$
    SELECT p.estado, COUNT(*)::BIGINT, COALESCE(SUM(p.total), 0)
    FROM public.pedido p
    GROUP BY p.estado;
$$;

CREATE OR REPLACE PROCEDURE public.sp_cambiar_estado_pedido(
    IN p_id_pedido BIGINT,
    IN p_estado_nuevo VARCHAR(25),
    IN p_id_empleado BIGINT,
    IN p_motivo TEXT DEFAULT NULL)
LANGUAGE plpgsql
AS $$
DECLARE
    v_estado_actual VARCHAR(25);
    v_tipo_entrega VARCHAR(20);
    v_rol VARCHAR(20);
BEGIN
    SELECT e.rol INTO v_rol
    FROM public.empleado e JOIN public.usuario u ON u.id_usuario = e.id_empleado
    WHERE e.id_empleado = p_id_empleado AND u.activo = TRUE;
    IF v_rol IS NULL OR v_rol NOT IN ('ADMINISTRADOR', 'OPERADOR') THEN
        RAISE EXCEPTION 'Empleado no autorizado o inactivo';
    END IF;

    SELECT p.estado, p.tipo_entrega INTO v_estado_actual, v_tipo_entrega
    FROM public.pedido p WHERE p.id_pedido = p_id_pedido FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Pedido % inexistente', p_id_pedido;
    END IF;

    IF NOT (
      (v_estado_actual = 'Recibido' AND p_estado_nuevo IN ('En preparación', 'Cancelado')) OR
      (v_estado_actual = 'En preparación' AND p_estado_nuevo = 'Cancelado') OR
      (v_estado_actual = 'En preparación' AND v_tipo_entrega = 'Domicilio' AND p_estado_nuevo = 'Enviado') OR
      (v_estado_actual = 'En preparación' AND v_tipo_entrega = 'Recolección' AND p_estado_nuevo = 'Listo para recoger') OR
      (v_estado_actual = 'Enviado' AND p_estado_nuevo = 'Entregado') OR
      (v_estado_actual = 'Listo para recoger' AND p_estado_nuevo = 'Entregado')
    ) THEN
        RAISE EXCEPTION 'Transición no permitida: % -> %', v_estado_actual, p_estado_nuevo;
    END IF;
    IF p_estado_nuevo = 'Cancelado' AND NULLIF(BTRIM(p_motivo), '') IS NULL THEN
        RAISE EXCEPTION 'Debe indicarse motivo de cancelación';
    END IF;

    UPDATE public.pedido
       SET estado = p_estado_nuevo,
           fecha_entrega = CASE WHEN p_estado_nuevo = 'Entregado' THEN CURRENT_TIMESTAMP ELSE fecha_entrega END
     WHERE id_pedido = p_id_pedido;

    INSERT INTO public.historial_estado
        (id_pedido, id_empleado, estado_anterior, estado_nuevo, motivo)
    VALUES (p_id_pedido, p_id_empleado, v_estado_actual, p_estado_nuevo, p_motivo);

    IF p_estado_nuevo = 'Entregado' THEN
        INSERT INTO public.registro_venta (id_pedido, estado_sincronizacion)
        VALUES (p_id_pedido, 'Pendiente')
        ON CONFLICT (id_pedido) DO NOTHING;
    END IF;
END;
$$;

CREATE OR REPLACE PROCEDURE public.sp_registrar_resultado_impresion(
    IN p_id_comanda BIGINT,
    IN p_exito BOOLEAN)
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE public.comanda
       SET estado_impresion = CASE WHEN p_exito THEN 'Enviada' ELSE 'Fallida' END,
           intentos = intentos + 1
     WHERE id_comanda = p_id_comanda;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Comanda % inexistente', p_id_comanda;
    END IF;
END;
$$;

REVOKE ALL ON FUNCTION public.fn_pedido_es_modificable(TEXT) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.fn_subtotal_detalles(BIGINT) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.fn_resumen_pedidos_por_estado() FROM PUBLIC;
REVOKE ALL ON PROCEDURE public.sp_cambiar_estado_pedido(BIGINT, VARCHAR, BIGINT, TEXT) FROM PUBLIC;
REVOKE ALL ON PROCEDURE public.sp_registrar_resultado_impresion(BIGINT, BOOLEAN) FROM PUBLIC;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'loyorder_api') THEN
        CREATE ROLE loyorder_api NOLOGIN;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'loyorder_reportes') THEN
        CREATE ROLE loyorder_reportes NOLOGIN;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'loyorder_app_user') THEN
        CREATE ROLE loyorder_app_user LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'loyorder_report_user') THEN
        CREATE ROLE loyorder_report_user LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
    END IF;
END $$;

GRANT loyorder_api TO loyorder_app_user;
GRANT loyorder_reportes TO loyorder_report_user;
GRANT CONNECT ON DATABASE loyorder TO loyorder_api, loyorder_reportes;
GRANT USAGE ON SCHEMA public TO loyorder_api, loyorder_reportes;

GRANT SELECT ON ALL TABLES IN SCHEMA public TO loyorder_api;
GRANT INSERT, UPDATE ON public.usuario, public.cliente, public.empleado,
    public.direccion, public.carrito, public.item_carrito,
    public.pedido, public.detalle_pedido, public.historial_estado,
    public.modificacion_pedido, public.comanda, public.registro_venta,
    public.notificacion, public.sincronizacion, public.categoria,
    public.producto, public.variante_producto, public.metodo_pago,
    public.restaurante, public.impresora, public.estacion_preparacion,
    public.asignacion_estacion TO loyorder_api;
GRANT DELETE ON public.item_carrito, public.carrito, public.direccion
    TO loyorder_api;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO loyorder_api;
GRANT EXECUTE ON FUNCTION public.fn_pedido_es_modificable(TEXT) TO loyorder_api, loyorder_reportes;
GRANT EXECUTE ON FUNCTION public.fn_subtotal_detalles(BIGINT) TO loyorder_api;
GRANT EXECUTE ON FUNCTION public.fn_resumen_pedidos_por_estado() TO loyorder_api;
GRANT EXECUTE ON PROCEDURE public.sp_cambiar_estado_pedido(BIGINT, VARCHAR, BIGINT, TEXT) TO loyorder_api;
GRANT EXECUTE ON PROCEDURE public.sp_registrar_resultado_impresion(BIGINT, BOOLEAN) TO loyorder_api;


GRANT SELECT ON public.vw_ventas_entregadas TO loyorder_reportes;

COMMIT;
