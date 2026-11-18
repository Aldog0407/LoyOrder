/** Mapbox uses longitude first; legacy percentage pins are not coordinates. */
export function isValidPin(pin) {
  return !!pin && Number.isFinite(pin.lng) && Number.isFinite(pin.lat) &&
    Math.abs(pin.lng) <= 180 && Math.abs(pin.lat) <= 85.051129;
}

export function normalizeAddress(address, pin) {
  if (!isValidPin(pin) || typeof address !== 'string' || address.trim().length < 8 || address.trim().length > 160) return null;
  return { address: address.trim(), pin: { lng: pin.lng, lat: pin.lat } };
}
