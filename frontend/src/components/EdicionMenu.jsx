import { useEffect, useState } from 'react';
import { peticionAutenticada } from '../auth.js';
import './EdicionMenu.css';

const consultar = () => peticionAutenticada('/admin/menu', null, 'GET');

export default function EdicionMenu() {
  const [productos, setProductos] = useState([]);
  const [seleccion, setSeleccion] = useState('');
  const [datos, setDatos] = useState(null);
  const [cargando, setCargando] = useState(true);
  const [guardando, setGuardando] = useState(false);
  const [error, setError] = useState('');
  const [aviso, setAviso] = useState('');

  useEffect(() => {
    let vigente = true;
    consultar().then(lista => {
      if (vigente) setProductos(lista);
    }).catch(fallo => {
      if (vigente) setError(fallo.message);
    }).finally(() => {
      if (vigente) setCargando(false);
    });
    return () => { vigente = false; };
  }, []);

  const producto = productos.find(p => String(p.id) === seleccion);

  function seleccionar(id) {
    setSeleccion(id);
    setDatos(productos.find(p => String(p.id) === id)?.datosLocales ?? null);
    setError('');
    setAviso('');
  }

  async function guardar(evento) {
    evento.preventDefault();
    if (!producto || guardando) return;
    setGuardando(true);
    setError('');
    setAviso('');
    const datosLocales = {
      nombre: datos.nombre?.trim() || null,
      descripcion: datos.descripcion?.trim() || null,
      imagenUrl: datos.imagenUrl?.trim() || null,
    };
    try {
      await peticionAutenticada(`/admin/menu/${producto.id}/editar`, datosLocales);
      setProductos(lista => lista.map(p => p.id === producto.id ? { ...p, datosLocales } : p));
      setDatos(datosLocales);
      setAviso('Cambios del menú guardados.');
    } catch (fallo) {
      setError(fallo.message);
    } finally {
      setGuardando(false);
    }
  }

  return <section className="surface settings-card edicion-menu" aria-labelledby="edicion-menu-titulo">
    <h2 id="edicion-menu-titulo">Editar menú</h2>
    {cargando && <p role="status">Cargando productos…</p>}
    {!cargando && !error && productos.length === 0 && <p>Sin productos disponibles. Sincroniza el catálogo en Loyverse.</p>}
    {productos.length > 0 && <label className="settings-field">Producto
      <select value={seleccion} onChange={evento => seleccionar(evento.target.value)} disabled={guardando}>
        <option value="">Seleccionar producto</option>
        {productos.map(p => <option key={p.id} value={p.id}>{p.categoria} · {p.datosLocales.nombre ?? p.nombreOrigen}</option>)}
      </select>
    </label>}
    {producto && datos && <form onSubmit={guardar}>
      <p className="settings-hint">Deja un campo en blanco para usar el dato de Loyverse.</p>
      <label className="settings-field">Nombre en el menú
        <input maxLength={150} value={datos.nombre ?? ''} placeholder={producto.nombreOrigen}
          onChange={evento => setDatos({ ...datos, nombre: evento.target.value })} disabled={guardando}/>
      </label>
      <label className="settings-field">Descripción
        <textarea rows={4} maxLength={2000} value={datos.descripcion ?? ''} placeholder={producto.descripcionOrigen ?? ''}
          onChange={evento => setDatos({ ...datos, descripcion: evento.target.value })} disabled={guardando}/>
      </label>
      <label className="settings-field">URL de imagen
        <input type="url" maxLength={2048} value={datos.imagenUrl ?? ''} placeholder={producto.imagenOrigen ?? 'https://'}
          onChange={evento => setDatos({ ...datos, imagenUrl: evento.target.value })} disabled={guardando}/>
      </label>
      <button type="submit" className="button primary" disabled={guardando}>{guardando ? 'Guardando…' : 'Guardar cambios'}</button>
    </form>}
    {error && <p className="checkout-error" role="alert">{error}</p>}
    {aviso && <p role="status">{aviso}</p>}
  </section>;
}
