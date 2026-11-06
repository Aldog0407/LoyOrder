export function isOpen(config, now = new Date()) {
  const toMinutes = time => Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5));
  const start = toMinutes(config.openingTime), end = toMinutes(config.closingTime);
  const current = now.getHours() * 60 + now.getMinutes();
  if (start === end) return false;
  return start < end ? current >= start && current < end : current >= start || current < end;
}

// Hasta integrar un proveedor de mapas se cobra solo la tarifa base.
export function shippingFee(config, deliveryMode, distanceKm = null) {
  if (deliveryMode !== 'domicilio') return 0;
  const { base, mode, radiusKm, perKm } = config.shipping;
  if (mode !== 'distance' || distanceKm === null) return base;
  return base + Math.max(0, distanceKm - radiusKm) * perKm;
}

export function validateConfig(config) {
  if (!/^([01]\d|2[0-3]):[0-5]\d$/.test(config.openingTime) || !/^([01]\d|2[0-3]):[0-5]\d$/.test(config.closingTime)) return 'Introduce horarios válidos.';
  if (config.openingTime === config.closingTime) return 'La apertura y cierre deben ser distintos.';
  if (['base', 'radiusKm', 'perKm'].some(k => !Number.isFinite(config.shipping[k]) || config.shipping[k] < 0 || config.shipping[k] > 10000)) return 'Importes y distancias deben estar entre 0 y 10 000.';
  if (!Object.values(config.paymentMethods).some(Boolean)) return 'Habilita al menos un método de pago.';
  return null;
}
