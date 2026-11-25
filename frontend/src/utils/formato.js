const pesos = new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN', maximumFractionDigits: 2 });

export const money = amount => pesos.format(amount);

/** "hoy a las 18:04" o "24 nov, 18:04" para la fecha de la última sincronización. */
export function horaActualizacion(fechaIso, ahora = new Date()) {
  const fecha = new Date(fechaIso);
  if (!fechaIso || Number.isNaN(fecha.getTime())) return null;
  const hora = fecha.toLocaleTimeString('es-MX', { hour: '2-digit', minute: '2-digit' });
  return fecha.toDateString() === ahora.toDateString()
    ? `hoy a las ${hora}`
    : `${fecha.toLocaleDateString('es-MX', { day: 'numeric', month: 'short' })}, ${hora}`;
}
