const BASE_URL = 'https://api.mapbox.com/search/geocode/v6';
const PUBLIC_TOKEN = import.meta.env?.VITE_MAPBOX_ACCESS_TOKEN?.trim();
const DEFAULT_COOLDOWN_MS = 60_000;
const MAX_COOLDOWN_MS = 5 * 60_000;
let blockedUntil = 0;

export class GeocodingRateLimitError extends Error {
  constructor(waitMs) {
    super(`Mapbox limitó temporalmente las consultas (HTTP 429). Espera ${Math.ceil(waitMs / 1000)} segundos o introduce la dirección manualmente.`);
    this.name = 'GeocodingRateLimitError';
    this.status = 429;
    this.waitMs = waitMs;
  }
}

export function geocodingWaitMs() {
  return Math.max(0, blockedUntil - Date.now());
}

function getToken(token = PUBLIC_TOKEN) {
  if (!token?.startsWith('pk.')) throw new Error('Configura VITE_MAPBOX_ACCESS_TOKEN con un token público de Mapbox.');
  return token;
}

function retryDelay(headers) {
  const retryAfter = headers?.get?.('Retry-After');
  if (retryAfter) {
    const seconds = Number(retryAfter);
    const time = Number.isFinite(seconds) ? seconds * 1000 : Date.parse(retryAfter) - Date.now();
    if (Number.isFinite(time) && time > 0) return Math.min(MAX_COOLDOWN_MS, Math.max(1000, time));
  }
  return DEFAULT_COOLDOWN_MS;
}

async function requestMapbox(path, params, { signal, token, fetchImpl = fetch } = {}) {
  const waitMs = geocodingWaitMs();
  if (waitMs > 0) throw new GeocodingRateLimitError(waitMs);
  const url = new URL(`${BASE_URL}/${path}`);
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') url.searchParams.set(key, String(value));
  }
  url.searchParams.set('access_token', getToken(token));
  // Checkout stores the selected geocode in localStorage and submits it to the backend.
  // Mapbox permanent geocoding must be enabled for this account; it may be billable.
  url.searchParams.set('permanent', 'true');
  let response;
  try {
    response = await fetchImpl(url.toString(), { signal, cache: 'no-store' });
  } catch (error) {
    if (error.name === 'AbortError') throw error;
    throw new Error('No hay conexión con el servicio de direcciones. Puedes completar el domicilio manualmente.');
  }
  if (response.status === 429) {
    const delay = retryDelay(response.headers);
    blockedUntil = Math.max(blockedUntil, Date.now() + delay);
    throw new GeocodingRateLimitError(delay);
  }
  if (response.status === 401 || response.status === 403) {
    throw new Error('Mapbox rechazó la consulta (HTTP ' + response.status + '). Comprueba el token y los permisos de geocodificación permanente.');
  }
  if (!response.ok) throw new Error(`No fue posible consultar direcciones (HTTP ${response.status}).`);
  return response.json();
}

function readableAddress(properties = {}) {
  return (properties.full_address || [properties.name_preferred || properties.name, properties.place_formatted].filter(Boolean).join(', ')).trim();
}

/** Only use structured attributes supplied by Mapbox; never invent an exterior number. */
export function extraerComponentes(feature = {}) {
  const properties = feature.properties ?? {};
  const context = properties.context ?? {};
  const calle = properties.street || context.street?.name || context.address?.street_name || '';
  const numero = properties.address_number || context.address?.address_number || '';
  return { calle: String(calle).trim(), numero: String(numero).trim() };
}

export function normalizarSugerencias(features = []) {
  if (!Array.isArray(features)) return [];
  return features.flatMap(feature => {
    const coords = feature.properties?.coordinates;
    const lng = coords?.longitude ?? feature.geometry?.coordinates?.[0];
    const lat = coords?.latitude ?? feature.geometry?.coordinates?.[1];
    const address = readableAddress(feature.properties);
    if (!address || !Number.isFinite(lng) || !Number.isFinite(lat) || Math.abs(lng) > 180 || Math.abs(lat) > 85.051129) return [];
    return [{
      id: feature.properties?.mapbox_id ?? feature.id ?? `${lng}:${lat}:${address}`,
      address,
      label: feature.properties?.name_preferred || feature.properties?.name || address,
      detail: feature.properties?.place_formatted || '',
      checkoutAddress: extraerComponentes(feature),
      pin: { lng, lat }
    }];
  });
}

export async function buscarDirecciones(query, { signal, proximity, token, fetchImpl } = {}) {
  const text = String(query ?? '').trim();
  if (text.length < 4) return [];
  const params = {
    q: text, language: 'es', country: 'mx', autocomplete: 'true', limit: 5,
    types: 'address,street,neighborhood,locality,place'
  };
  if (Number.isFinite(proximity?.lng) && Number.isFinite(proximity?.lat)) {
    params.proximity = `${proximity.lng},${proximity.lat}`;
  }
  const data = await requestMapbox('forward', params, { signal, token, fetchImpl });
  return normalizarSugerencias(data.features);
}

/** Detailed reverse geocoding: structured fields and display text for the modal. */
export async function reverseGeocodeDetalle(lng, lat, signal, options = {}) {
  if (!Number.isFinite(lng) || !Number.isFinite(lat) || Math.abs(lng) > 180 || Math.abs(lat) > 85.051129) {
    throw new Error('Coordenadas inválidas.');
  }
  const data = await requestMapbox('reverse', {
    longitude: lng, latitude: lat, language: 'es', country: 'mx', types: 'address,street'
  }, { ...options, signal });
  const feature = data.features?.find(item => item.properties?.feature_type === 'address') ?? data.features?.[0];
  const address = readableAddress(feature?.properties);
  if (!address) throw new Error('No se encontró una dirección. Puedes escribirla manualmente.');
  return { address, checkoutAddress: extraerComponentes(feature) };
}

/** Retain old string return contract for existing consumers and tests. */
export async function reverseGeocode(lng, lat, signal, options = {}) {
  return (await reverseGeocodeDetalle(lng, lat, signal, options)).address;
}
