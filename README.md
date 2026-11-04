# LoyOrder

Sistema web de pedidos para restaurantes, integrado con Loyverse POS.

## Propósito
Permitir a clientes realizar pedidos y al personal del restaurante administrarlos, con integración a Loyverse y servicios externos.

## Organización inicial

- `frontend/`: interfaz para cliente, caja y administración.
- `backend/`: API REST organizada por capas.
- `database/`: scripts y cambios de base de datos.
- `docs/`: documentación y diagramas del proyecto.
- `tests/`: pruebas de la aplicación.

La tecnología definitiva del frontend, backend y base de datos se incorpora una vez confirmada la decisión de la tarea 1.2.6.

## Rama de trabajo

Todo el equipo trabaja directamente en `main`. Las reglas están en `docs/REGLAS_GIT.md`.

## Convención de commits

Formato: `tipo: descripción breve`

- `feat:` funcionalidad nueva.
- `fix:` corrección de error.
- `docs:` documentación.
- `test:` pruebas.
- `refactor:` mejora interna sin cambiar comportamiento.
- `chore:` configuración y mantenimiento.

Ejemplos: `chore: crear estructura inicial`, `docs: agregar diagrama de casos de uso`.

## Flujo de trabajo

1. Traer los cambios del equipo: `git pull origin main`.
2. Hacer los cambios y comprobar que compilan y pasan las pruebas.
3. Hacer commit con un mensaje claro.
4. Traer cambios otra vez con `git pull --rebase origin main` y resolver conflictos si aparecen.
5. Subir con `git push origin main` y avisar al equipo qué se subió.

## Seguridad

No subir contraseñas, tokens de Loyverse, archivos `.env` reales ni datos sensibles al repositorio.
