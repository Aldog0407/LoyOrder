/** Configuración local. Estos códigos son adaptadores de demo, no IDs oficiales Loyverse. */
export const defaultSettings = {
  open: '09:00', close: '22:00',
  delivery: { mode: 'flat', base: 35, radiusKm: 5, perKm: 8 },
  payments: { cash: true, card: true, digital: false },
  printer: { connected: false, name: 'Impresora térmica (demo)' },
};

export const paymentCatalog = [
  { key: 'cash', label: 'Efectivo', emoji: '💵', loyverseType: 'CASH' },
  { key: 'card', label: 'Tarjeta crédito / débito', emoji: '💳', loyverseType: 'CARD' },
  { key: 'digital', label: 'Pagos digitales', emoji: '📲', loyverseType: 'OTHER' },
];

export const copySettings = value => JSON.parse(JSON.stringify(value));
const minutes = time => Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5));

/** Intervalos nocturnos: 18:00 → 02:00. Apertura = cierre se considera cerrado. */
export function isRestaurantOpen(config, now = new Date()) {
  const current = now.getHours() * 60 + now.getMinutes();
  const start = minutes(config.open), end = minutes(config.close);
  return start === end ? false : start < end ? current >= start && current < end : current >= start || current < end;
}

/** Sin distancia geocodificada, se utiliza la base; nunca se inventan kilómetros. */
export function shippingCost(config, kilometers = null) {
  const { delivery } = config;
  if (delivery.mode === 'flat' || kilometers === null) return delivery.base;
  if (!Number.isFinite(kilometers) || kilometers < 0) throw new RangeError('Distancia inválida');
  return delivery.base + Math.max(0, kilometers - delivery.radiusKm) * delivery.perKm;
}

export function validateSettings(config) {
  if (!/^([01]\d|2[0-3]):[0-5]\d$/.test(config.open) || !/^([01]\d|2[0-3]):[0-5]\d$/.test(config.close)) return 'Introduce horarios válidos.';
  if (config.open === config.close) return 'La apertura y el cierre deben ser diferentes.';
  for (const field of ['base', 'radiusKm', 'perKm']) {
    if (!Number.isFinite(config.delivery[field]) || config.delivery[field] < 0 || config.delivery[field] > 10000) return 'Los importes y distancias deben estar entre 0 y 10 000.';
  }
  if (!Object.values(config.payments).some(Boolean)) return 'Habilita por lo menos un método de pago.';
  return null;
}

export function renderSettings(config) {
  const open = isRestaurantOpen(config);
  return `<main class="container page settings-page">
    <div class="settings-heading"><div><span class="eyebrow orange">ADMINISTRACIÓN / AJUSTES</span><h1>Configuración del restaurante</h1><p>Controla la operación y los métodos de cobro desde un solo lugar.</p></div><span class="badge ${open ? 'success' : 'error'}" data-open-status>${open ? '● Abierto ahora' : '● Cerrado ahora'}</span></div>
    <form id="settings-form" class="settings-grid">
      <section class="surface settings-card"><div class="settings-card-head"><span class="settings-symbol">◷</span><div><h2>Horarios de operación</h2><p>Horario local del dispositivo. Admite turnos que cruzan medianoche.</p></div></div><div class="settings-two"><label class="settings-field">Hora de apertura<input type="time" name="open" value="${config.open}" required></label><label class="settings-field">Hora de cierre<input type="time" name="close" value="${config.close}" required></label></div><div class="settings-hint">El indicador se actualiza automáticamente durante la sesión.</div></section>
      <section class="surface settings-card"><div class="settings-card-head"><span class="settings-symbol">🛵</span><div><h2>Costo de envío</h2><p>Define la base y el modelo de cálculo.</p></div></div><label class="settings-field">Tarifa base (MXN)<input type="number" name="base" min="0" max="10000" step="0.01" value="${config.delivery.base}" required></label><div class="settings-radios"><label><input type="radio" name="shippingMode" value="flat" ${config.delivery.mode === 'flat' ? 'checked' : ''}> Tarifa fija</label><label><input type="radio" name="shippingMode" value="distance" ${config.delivery.mode === 'distance' ? 'checked' : ''}> Por kilómetro</label></div><div class="settings-two distance-fields" ${config.delivery.mode === 'flat' ? 'hidden' : ''}><label class="settings-field">Radio incluido (km)<input type="number" name="radiusKm" min="0" max="10000" step="0.1" value="${config.delivery.radiusKm}" required></label><label class="settings-field">Extra por km (MXN)<input type="number" name="perKm" min="0" max="10000" step="0.01" value="${config.delivery.perKm}" required></label></div><div class="settings-hint">Sin integración de distancias reales, el checkout cobra únicamente la tarifa base.</div></section>
      <section class="surface settings-card"><div class="settings-card-head"><span class="settings-symbol">💳</span><div><h2>Métodos de pago</h2><p>Catálogo conceptual compatible con tipos de Loyverse.</p></div></div><div class="settings-payment-list">${paymentCatalog.map(method => `<label class="settings-payment"><span><b>${method.emoji} ${method.label}</b><small>Tipo de referencia: ${method.loyverseType}</small></span><input type="checkbox" name="payment-${method.key}" ${config.payments[method.key] ? 'checked' : ''} aria-label="Activar ${method.label}"></label>`).join('')}</div><div class="integration-note">◌ Sincronización con Loyverse no conectada. Las preferencias solo afectan esta demostración.</div></section>
      <section class="surface settings-card"><div class="settings-card-head"><span class="settings-symbol">🖨</span><div><h2>Impresora de caja</h2><p>Prueba el comportamiento del adaptador de impresión.</p></div></div><div class="printer-status"><div><strong>${config.printer.name}</strong><small>Estado de conexión (simulado)</small></div><span class="badge ${config.printer.connected ? 'success' : 'error'}" id="printer-badge">${config.printer.connected ? '● Conectada' : '● Desconectada'}</span></div><div class="printer-actions"><button type="button" class="button outline" data-action="printer-toggle">${config.printer.connected ? 'Desconectar demo' : 'Simular conexión'}</button><button type="button" class="button primary" data-action="printer-test" ${config.printer.connected ? '' : 'disabled'}>Imprimir ticket de prueba</button></div><div class="ticket-result" id="ticket-result" role="status" aria-live="polite">No hay pruebas realizadas en esta sesión.</div><div class="integration-note">La respuesta es simulada: no se envían comandos ESC/POS ni se descubre hardware.</div></section>
      <div class="settings-save"><span>Los ajustes se almacenan en este navegador.</span><button type="submit" class="button primary">Guardar configuración</button></div>
    </form>
  </main>`;
}

/** Adaptador de prueba: sustituir por POST /api/pos/print-test en backend real. */
export async function simulatePrint(config) {
  await new Promise(resolve => setTimeout(resolve, 650));
  if (!config.printer.connected) throw new Error('Impresora desconectada');
  return { ok: true, ticket: 'TEST-' + Date.now().toString(36).toUpperCase(), message: 'Ticket de ejemplo procesado por el simulador' };
}
