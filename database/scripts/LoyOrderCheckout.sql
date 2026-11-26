-- Ejecutar después del esquema y los permisos. Se puede repetir sobre la base existente.
BEGIN;
ALTER TABLE public.pedido ADD COLUMN IF NOT EXISTS id_operacion UUID;
ALTER TABLE public.pedido ADD COLUMN IF NOT EXISTS solicitud_hash BYTEA;
-- Conserva el avance de la secuencia y contempla datos semilla insertados con ID explícito.
SELECT setval(pg_get_serial_sequence('public.pedido','id_pedido'),
    GREATEST(COALESCE((SELECT MAX(id_pedido) FROM public.pedido),0),
        COALESCE(pg_sequence_last_value(pg_get_serial_sequence('public.pedido','id_pedido')::regclass),0),1), true);
CREATE UNIQUE INDEX IF NOT EXISTS uq_pedido_operacion_cliente
    ON public.pedido(id_cliente, id_operacion) WHERE id_operacion IS NOT NULL;
COMMIT;
