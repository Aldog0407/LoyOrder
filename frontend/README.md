# LoyOrder — Base frontend funcional

Prototipo de navegación del sistema de pedidos LoyOrder, desarrollado en **HTML5 + CSS3 + JavaScript ES Modules**, sin librerías de UI ni servidor obligatorio.

## Ejecutar

Los módulos ES requieren servir el proyecto sobre HTTP. Desde esta carpeta:

```bash
python3 dev_server.py
```

Abrir `http://localhost:5173/menu`. El servidor incluido ofrece fallback a `index.html` para que funcionen las rutas directas y la recarga. Con Live Server de VS Code se puede abrir la raíz del sitio, pero las rutas directas requieren configurar un fallback. Para GitHub Pages, que no soporta rewrites nativos, adapta las rutas a hash (`#/menu`) o usa una solución de fallback apropiada.

## Estructura

```text
loyorder/
├── index.html        # Entrada semántica
├── dev_server.py     # Servidor local con fallback de rutas
├── package.json      # Módulos ES y comprobación de sintaxis
├── styles.css        # Tokens :root, componentes y breakpoints
└── src/
    ├── data.js       # Catálogo y pedidos de ejemplo
    ├── settings.js   # Reglas, validación y adaptador POS simulado
    ├── store.js      # Estado y persistencia localStorage
    └── main.js       # Renderizado, rutas, eventos y flujos
```

## Rutas y flujos

| Ruta | Vista |
| --- | --- |
| `/menu` | Catálogo por categorías, disponibilidad, detalle con variante y cantidad |
| `/seguimiento` | Timeline de pedido y calificación al entregarse |
| `/historial` | Listado, reordenar con confirmación y acceso a seguimiento |
| `/admin` | KPI de demostración, periodos y últimos pedidos |
| `/admin/configuracion` | Horarios, envío, medios de pago y simulador de impresora |

El carrito es lateral en escritorio y pantalla completa en móvil. Incluye modos de entrega, dirección, método de pago, desglose y confirmación. El selector de mapa es **esquemático** con pin arrastrable y campo de dirección; no geocodifica ni consulta mapas reales.

## Estado de integración

- **Funcional en frontend:** rutas internas, filtros, modal de platillo, controles de cantidad, variantes obligatorias, carrito, selección de domicilio/local, selector de ubicación, confirmación, timeline, simulación de avance, historial, reordenación, calificación, dashboard, notificaciones y persistencia local.
- **Configuración:** horario con indicador abierto/cerrado (reloj del dispositivo, incluso turnos nocturnos), tarifa base y esquema por radio/kilómetro, activación de métodos de pago y simulación de conexión/prueba de impresora. Los pagos activos y la tarifa base alimentan el checkout. No se calculan kilómetros reales sin geocodificación.
- **Simulado / no productivo:** autenticación, disponibilidad de inventario, cobro con tarjeta, envío de correos, persistencia multiusuario, métricas de ventas, ubicación geográfica real y actualizaciones de estado desde cocina. **No se realiza ningún cobro real.**
- **Loyverse/POS:** `src/settings.js` contiene equivalencias conceptuales `CASH`, `CARD`, `OTHER`, no identificadores de métodos del comercio. Sin credenciales, servidor, conexión Loyverse o impresora física. La respuesta de impresión es una promesa simulada. No se afirma conectividad en tiempo real con hardware.
- **Integración pendiente:** API REST, login y autorización de roles, base de datos, proveedor de mapas/geocodificación, pagos tokenizados en backend, estados emitidos por cocina y notificaciones.

## Tokens

Los colores, radios, sombras y fuente están en `:root` de `styles.css`, conforme a la guía proporcionada. Breakpoints principales: 1000px, 760px y 390px.

## Pruebas manuales recomendadas

1. Filtrar categorías, abrir un platillo y verificar que se exige una variante.
2. Agregar varias unidades, editar y eliminar del carrito.
3. Seleccionar domicilio, ajustar pin, introducir dirección y confirmar.
4. Realizar pedido de prueba y avanzar su estado hasta Entregado.
5. Calificar el pedido, visitar historial y reordenar.
6. Verificar `/admin` y los periodos Hoy / Semana / Mes.
7. Probar anchos 375px, 768px y 1440px, y navegación con botón Atrás.
8. En `/admin/configuracion`, guardar horarios nocturnos, validar valores negativos, deshabilitar métodos, alternar tarifa y probar la conexión e impresión simulada.

## Nota de producción

Este entregable corresponde únicamente al **proyecto base funcional del frontend**. La interfaz no constituye un sistema de pedidos operativo y los datos del navegador pueden borrarse en cualquier momento.
