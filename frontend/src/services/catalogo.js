import { peticionAutenticada } from '../auth.js';

const consultar = ruta => peticionAutenticada(ruta, null, 'GET');

// Actividad 7.2.4 · Catálogo por categorías. El menú viene de GET /api/v1/catalogo, que la API arma con lo que la
// sincronización (7.2.3) copió de Loyverse. El restaurante lo resuelve la API; el navegador nunca habla con Loyverse.

const mensajes = {
  401: 'Tu sesión terminó. Vuelve a iniciar sesión para ver el menú.',
  403: 'Tu cuenta no puede consultar el menú de clientes.',
  409: 'El restaurante aún no está configurado. Intenta más tarde.',
  503: 'El menú no está disponible en este momento. Intenta en unos minutos.',
};

const texto = valor => (typeof valor === 'string' && valor.trim() ? valor.trim() : null);
const id = valor => (Number.isSafeInteger(valor) && valor > 0 ? valor : null);
const imagen = valor => (typeof valor === 'string' && /^https?:\/\//i.test(valor) ? valor : null);

/** Valida y adapta la respuesta de la API; descarta lo que no se pueda vender (sin id, sin nombre o sin precio). */
export function normalizarCatalogo(datos) {
  const categorias = (Array.isArray(datos?.categorias) ? datos.categorias : []).map(categoria => {
    const productos = (Array.isArray(categoria?.productos) ? categoria.productos : []).map(producto => {
      const variantes = (Array.isArray(producto?.variantes) ? producto.variantes : [])
        .filter(v => id(v?.id) && Number.isFinite(v.precio) && v.precio > 0)
        .map(v => ({ id: v.id, nombre: texto(v.nombre), precio: v.precio, agotado: v.agotado === true }));
      if (!id(producto?.id) || !texto(producto.nombre) || !variantes.length) return null;
      const agotado = variantes.every(v => v.agotado);
      const precios = variantes.filter(v => agotado || !v.agotado).map(v => v.precio);
      return {
        id: producto.id,
        nombre: texto(producto.nombre),
        descripcion: texto(producto.descripcion),
        imagenUrl: imagen(producto.imagenUrl),
        idCategoria: categoria.id,
        categoria: texto(categoria.nombre),
        precioDesde: Math.min(...precios),
        precioVariable: new Set(variantes.map(v => v.precio)).size > 1,
        agotado,
        variantes,
      };
    }).filter(Boolean);
    return id(categoria?.id) && texto(categoria.nombre) && productos.length
      ? { id: categoria.id, nombre: texto(categoria.nombre), productos }
      : null;
  }).filter(Boolean);
  return { actualizadoEn: texto(datos?.actualizadoEn), categorias };
}

/** Descarga el menú. Lanza un Error con un mensaje para el cliente y el estatus HTTP en `status`. */
export async function obtenerCatalogo(consulta = consultar) {
  try {
    return normalizarCatalogo(await consulta('/catalogo'));
  } catch (error) {
    const amigable = new Error(mensajes[error.status] ?? error.message ?? 'No se pudo cargar el menú.');
    amigable.status = error.status;
    throw amigable;
  }
}
