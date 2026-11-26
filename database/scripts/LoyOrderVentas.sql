-- 7.2.13: aplicar después del esquema y objetos/permisos. Repetible.
BEGIN;
ALTER TABLE public.registro_venta ADD COLUMN IF NOT EXISTS iniciado_en TIMESTAMPTZ;
ALTER TABLE public.registro_venta ADD COLUMN IF NOT EXISTS proximo_intento TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE public.registro_venta ADD COLUMN IF NOT EXISTS proceso UUID;
ALTER TABLE public.registro_venta ADD COLUMN IF NOT EXISTS solicitud JSONB;
CREATE INDEX IF NOT EXISTS idx_venta_pendiente ON public.registro_venta(proximo_intento,id_registro)
    WHERE estado_sincronizacion <> 'Confirmado';
COMMIT;
