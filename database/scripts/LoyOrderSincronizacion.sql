-- LOYORDER | Actividad 7.2.3 | Sincronización de catálogo e inventario desde Loyverse (CU-10)
-- Migración: se ejecuta después de LoyOrderEsquema.sql y LoyOrderObjetos_y_Permisos.sql. Se puede repetir.
-- Agrega el resumen de cada corrida (CU-10 paso 7: artículos nuevos, actualizados y eliminados).
-- La API ya tiene INSERT y UPDATE sobre sincronizacion, categoria, producto, variante_producto y restaurante.
BEGIN;

ALTER TABLE public.sincronizacion
    ADD COLUMN IF NOT EXISTS resumen JSONB;

COMMENT ON COLUMN public.sincronizacion.estado IS 'En proceso, Correcta o Fallida (Simulada en los datos semilla)';
COMMENT ON COLUMN public.sincronizacion.resumen IS 'Conteos de la corrida: categorías, productos y variantes nuevos, actualizados y eliminados; existencias actualizadas';

COMMIT;
