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

## Convención de ramas

- `main`: versión revisada y estable.
- `develop`: integración del trabajo del equipo.
- `feature/<descripcion-corta>`: desarrollo de una funcionalidad.
- `fix/<descripcion-corta>`: corrección de un defecto.

Ejemplos: `feature/autenticacion`, `feature/carrito`, `fix/validacion-pedido`.

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

1. Actualizar `develop`.
2. Crear una rama de trabajo basada en `develop`.
3. Hacer cambios y commits con mensajes claros.
4. Subir la rama y abrir un Pull Request hacia `develop`.
5. Pedir revisión a otro integrante antes de fusionar.
6. Integrar en `main` únicamente una versión revisada.

## Seguridad

No subir contraseñas, tokens de Loyverse, archivos `.env` reales ni datos sensibles al repositorio.
