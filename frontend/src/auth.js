// Las credenciales viven en memoria de esta pestaña.
const base = (import.meta.env?.VITE_API_URL || 'http://localhost:5080/api/v1').replace(/\/$/, '');
let sesion = null;
let usuario = null;
let temporizador;
let aviso;
let renovacion;
let cerrando = false;
let ultimoIntento = 0;
let alVencer = () => {};
let alAdvertir = () => {};

export const actual = () => usuario;
export function cuandoVenza(fn) { alVencer = fn; }
export function cuandoSeAcerqueElVencimiento(fn) { alAdvertir = fn; }

async function peticion(ruta, datos, metodo = 'POST', token) {
  let respuesta;
  try {
    respuesta = await fetch(base + ruta, {
      method: metodo,
      headers: { ...(datos ? {'Content-Type':'application/json'} : {}), ...(token ? {Authorization:'Bearer '+token} : {}) },
      ...(datos ? {body:JSON.stringify(datos)} : {}),
      signal: AbortSignal.timeout(10000)
    });
  } catch {
    throw new Error('No se pudo conectar con la API. Comprueba que esté encendida en el puerto 5080.');
  }
  const cuerpo = await respuesta.json().catch(() => null);
  if (!respuesta.ok) {
    const detalle = cuerpo?.errors ? Object.values(cuerpo.errors).flat().join(' ') : cuerpo?.title;
    const error = new Error(respuesta.status === 429 ? 'Demasiados intentos. Espera un minuto.' : detalle || 'No se pudo completar la solicitud.');
    error.status = respuesta.status;
    error.codigo = cuerpo?.codigo;
    error.idDesafio = cuerpo?.idDesafio;
    throw error;
  }
  return cuerpo;
}

function finalizar(vencida) {
  const anterior = usuario;
  clearTimeout(temporizador);
  clearTimeout(aviso);
  sesion = null;
  usuario = null;
  alAdvertir(false);
  if (vencida && anterior) alVencer(anterior);
}

function programar(plazos) {
  clearTimeout(temporizador);
  clearTimeout(aviso);
  alAdvertir(false);
  const fin = Math.min(Date.parse(plazos.idleExpiresAt ?? plazos.expiresAt),
    Date.parse(plazos.sessionExpiresAt ?? plazos.expiresAt));
  const restante = Math.max(0, fin - Date.now());
  aviso = setTimeout(() => {
    if (sesion === plazos) alAdvertir(true);
  }, Math.max(0, restante - 120000));
  temporizador = setTimeout(() => {
    const cerrarSiNoRenovo = () => { if (sesion === plazos) finalizar(true); };
    if (renovacion) void renovacion.then(cerrarSiNoRenovo, cerrarSiNoRenovo);
    else cerrarSiNoRenovo();
  }, restante);
}

function renovar() {
  if (renovacion) return renovacion;
  const anterior = sesion;
  if (!anterior?.refreshToken) return Promise.resolve();
  ultimoIntento = Date.now();
  renovacion = peticion('/auth/renovar', { refreshToken: anterior.refreshToken })
    .then(nueva => {
      if (sesion !== anterior) return;
      sesion = nueva;
      programar(nueva);
    })
    .catch(error => {
      if (error.status === 401 && sesion === anterior) finalizar(true);
      throw error;
    })
    .finally(() => { renovacion = null; });
  return renovacion;
}

export function registrarActividad() {
  if (!sesion?.refreshToken || renovacion || cerrando) return;
  const ahora = Date.now();
  const tiempoDesdeIntento = ahora - ultimoIntento;
  const porVencer = Date.parse(sesion.expiresAt) - ahora <= 10000;
  if (tiempoDesdeIntento < 5000 || (tiempoDesdeIntento < 60000 && !porVencer)) return;
  // Un fallo temporal se puede volver a intentar cuando haya actividad real.
  void renovar().catch(() => {});
}

export async function registrar(datos) { return peticion('/auth/registro', datos); }

export async function iniciar(datos) {
  const nueva = await peticion('/auth/login', datos);
  const identidad = await peticion('/auth/me', null, 'GET', nueva.accessToken);
  sesion = nueva;
  usuario = identidad;
  ultimoIntento = Date.now();
  programar(nueva);
  return usuario;
}

export async function cerrar() {
  if (!sesion) return;
  cerrando = true;
  try {
    if (renovacion) await renovacion.catch(() => {});
    if (sesion?.refreshToken)
      await peticion('/auth/logout', { refreshToken: sesion.refreshToken });
    else if (sesion)
      await peticion('/auth/logout', null, 'POST', sesion.accessToken);
  } catch (error) {
    if (error.status !== 401) throw error;
  } finally {
    cerrando = false;
  }
  finalizar(false);
}
