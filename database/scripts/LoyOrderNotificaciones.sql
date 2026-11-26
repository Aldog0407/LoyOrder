-- Avisos persistidos por evento. Aplicar después del esquema y permisos.
BEGIN;
ALTER TABLE public.notificacion ADD COLUMN IF NOT EXISTS id_historial BIGINT REFERENCES public.historial_estado(id_historial);
ALTER TABLE public.notificacion ADD COLUMN IF NOT EXISTS intentos SMALLINT NOT NULL DEFAULT 0;
ALTER TABLE public.notificacion ADD COLUMN IF NOT EXISTS proximo_intento TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE public.notificacion ADD COLUMN IF NOT EXISTS ultimo_error TEXT;
CREATE UNIQUE INDEX IF NOT EXISTS uq_notificacion_historial ON public.notificacion(id_historial) WHERE id_historial IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_notificacion_pendiente ON public.notificacion(proximo_intento,id_notificacion)
    WHERE id_historial IS NOT NULL AND estado_envio IN ('Pendiente','Fallida') AND intentos < 5;
COMMIT;
