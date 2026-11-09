BEGIN;

ALTER TABLE public.usuario
    ADD COLUMN IF NOT EXISTS correo_verificado_en TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS version_sesion INTEGER NOT NULL DEFAULT 0;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'public.usuario'::regclass
          AND conname = 'usuario_version_sesion_no_negativa'
    ) THEN
        ALTER TABLE public.usuario
            ADD CONSTRAINT usuario_version_sesion_no_negativa CHECK (version_sesion >= 0);
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS public.desafio_correo (
    id_desafio UUID PRIMARY KEY,
    id_usuario BIGINT NOT NULL REFERENCES public.usuario(id_usuario) ON DELETE CASCADE,
    correo VARCHAR(150) NOT NULL,
    proposito VARCHAR(30) NOT NULL CHECK (proposito IN (
        'VERIFICAR_CORREO', 'CAMBIAR_CONTRASENA', 'RESTABLECER_CONTRASENA'
    )),
    secreto_hash BYTEA NOT NULL CHECK (octet_length(secreto_hash) = 32),
    creado_en TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    expira_en TIMESTAMPTZ NOT NULL,
    usado_en TIMESTAMPTZ,
    invalidado_en TIMESTAMPTZ,
    intentos SMALLINT NOT NULL DEFAULT 0 CHECK (intentos BETWEEN 0 AND 5),
    CONSTRAINT desafio_correo_vigencia CHECK (expira_en > creado_en),
    CONSTRAINT desafio_correo_cierre_exclusivo CHECK (usado_en IS NULL OR invalidado_en IS NULL)
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_desafio_correo_pendiente
    ON public.desafio_correo (id_usuario, proposito)
    WHERE usado_en IS NULL AND invalidado_en IS NULL;

CREATE INDEX IF NOT EXISTS idx_desafio_correo_expiracion
    ON public.desafio_correo (expira_en)
    WHERE usado_en IS NULL AND invalidado_en IS NULL;

GRANT SELECT, INSERT, UPDATE ON public.desafio_correo TO loyorder_api;

CREATE INDEX IF NOT EXISTS idx_desafio_correo_historial
    ON public.desafio_correo (id_usuario, proposito, creado_en DESC);

COMMIT;
