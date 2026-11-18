# LoyOrder — Req. 11

Parche para Proyecto LoyOrder(1).zip. Copia los archivos de frontend/ sobre tu frontend actual. Si tienes variables adicionales en .env.example, conserva sus valores. No sustituye el backend, package.json ni package-lock.json.

```bash
cd frontend
npm install mapbox-gl@^3
```

Agrega a `.env.local` (conserva VITE_API_URL si ya existe):

```dotenv
VITE_MAPBOX_ACCESS_TOKEN=pk.TU_TOKEN_PUBLICO
```

Reinicia Vite (`npm run dev`). Crea un token público específico en https://account.mapbox.com/access-tokens/ y restringe los orígenes de desarrollo y producción. Nunca uses un token sk.* en Vite. El CSS de Mapbox se importa desde MapAddressSelector.jsx.

El mapa abre en Xalapa como referencia inicial no seleccionada. El usuario debe elegir un punto y escribir la dirección manualmente. No hay geocodificación, catálogo de direcciones ni cálculo de distancias. Las coordenadas elegidas y el texto se guardan con SET_ADDRESS; se conservan al recargar y se limpian con el comportamiento existente de cierre de sesión. Los pins antiguos {x,y} se descartan únicamente de la dirección activa; el historial se conserva. El envío continúa utilizando la tarifa anterior. El enlace de seguimiento abre OpenStreetMap únicamente como visor externo opcional, la selección se realiza con Mapbox.

```bash
node --test tests/address.test.js tests/checkout.test.js
npm run build
```

Resultados: 8 pruebas de lógica aprobadas. El entorno de generación no pudo acceder al registro npm (EAI_AGAIN); por ello no se verificó el build con Mapbox ni una sesión real en navegador. No se incluye token ni lockfile modificado. Verifica que el mapa cargue, clic y arrastre funcionen, cancelar no cambie el estado, recargar conserve la ubicación, y que recolección no requiera coordenadas.
