const MAPBOX_TOKEN =
  import.meta.env.VITE_MAPBOX_ACCESS_TOKEN?.trim();

const GEOCODING_URL =
  'https://api.mapbox.com/search/geocode/v6/reverse';

/**
 * Convierte coordenadas geográficas en una dirección legible.
 *
 * @param {number} lng Longitud
 * @param {number} lat Latitud
 * @param {AbortSignal} signal Señal de cancelación
 * @returns {Promise<string>} Dirección encontrada
 */
export async function reverseGeocode(lng, lat, signal) {
  if (!MAPBOX_TOKEN?.startsWith('pk.')) {
    throw new Error('Token público de Mapbox no configurado.');
  }

  if (
    !Number.isFinite(lng) ||
    !Number.isFinite(lat) ||
    Math.abs(lng) > 180 ||
    Math.abs(lat) > 90
  ) {
    throw new Error('Coordenadas geográficas inválidas.');
  }

  const params = new URLSearchParams({
    longitude: String(lng),
    latitude: String(lat),
    access_token: MAPBOX_TOKEN,
    language: 'es',
    country: 'mx',
    types: 'address,street',
    permanent: 'false'
  });

  const response = await fetch(
    `${GEOCODING_URL}?${params.toString()}`,
    {
      method: 'GET',
      signal,
      cache: 'no-store'
    }
  );

  if (response.status === 429) {
    throw new Error(
      'Demasiadas consultas. Intenta nuevamente en unos segundos.'
    );
  }

  if (response.status === 401 || response.status === 403) {
    throw new Error(
      'Mapbox no autorizó la consulta de dirección.'
    );
  }

  if (!response.ok) {
    throw new Error('No fue posible consultar la dirección.');
  }

  const data = await response.json();

  // Priorizar una dirección sobre el nombre de una calle.
  const feature =
    data.features?.find(
      item => item.properties?.feature_type === 'address'
    ) ?? data.features?.[0];

  if (!feature) {
    throw new Error(
      'No se encontró una dirección para esta ubicación.'
    );
  }

  const properties = feature.properties;

  const address =
    properties.full_address ||
    [
      properties.name_preferred || properties.name,
      properties.place_formatted
    ].filter(Boolean).join(', ');

  if (!address?.trim()) {
    throw new Error('La dirección obtenida está incompleta.');
  }

  return address.trim();
}