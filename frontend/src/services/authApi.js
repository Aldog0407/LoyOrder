const base = (import.meta.env?.VITE_API_URL || 'http://localhost:5080/api/v1').replace(/\/$/, '');

export class ApiError extends Error {
  constructor(message, status, cuerpo, retryAfter) {
    super(message);
    this.status = status;
    this.codigo = cuerpo?.codigo;
    this.idDesafio = cuerpo?.idDesafio;
    this.retryAfter = retryAfter;
  }
}

export async function peticion(ruta, datos, metodo = 'POST', token, tipoRespuesta = 'json') {
  let respuesta;
  try {
    respuesta = await fetch(base + ruta, {
      method: metodo,
      headers: { ...(datos ? { 'Content-Type': 'application/json' } : {}), ...(token ? { Authorization: 'Bearer ' + token } : {}) },
      ...(datos ? { body: JSON.stringify(datos) } : {}),
      signal: AbortSignal.timeout(10000)
    });
  } catch {
    throw new Error('No se pudo conectar con la API. Comprueba que esté encendida.');
  }
  if (respuesta.ok && tipoRespuesta === 'blob') return respuesta.blob();
  const cuerpo = await respuesta.json().catch(() => ({}));
  if (!respuesta.ok) {
    const valor = respuesta.headers.get('Retry-After');
    const segundos = valor === null ? NaN : Number(valor);
    const retryAfter = Number.isFinite(segundos) ? Math.max(0, segundos) : undefined;
    const detalle = cuerpo?.errors ? Object.values(cuerpo.errors).flat().join(' ') : cuerpo?.title;
    const mensaje = respuesta.status === 429 ? 'Demasiados intentos. Espera antes de volver a intentar.' : detalle || 'No se pudo completar la solicitud.';
    throw new ApiError(mensaje, respuesta.status, cuerpo, retryAfter);
  }
  return cuerpo;
}

export const authApi = {
  register: datos => peticion('/auth/registro', datos),
  login: datos => peticion('/auth/login', datos),
  confirmEmail: datos => peticion('/auth/correo/confirmar', datos),
  resendEmail: datos => peticion('/auth/correo/reenviar', datos),
  requestPassword: datos => peticion('/auth/contrasena/solicitar', datos),
  resetPassword: datos => peticion('/auth/contrasena/restablecer', datos),
};
