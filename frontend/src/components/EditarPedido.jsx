import { useEffect, useState } from 'react';
import { agregarArticuloPedido, anularArticuloPedido, consultarEdicionPedido } from '../services/pedidos.js';
import { money } from '../utils/formato.js';
import Modal from './Modal.jsx';
import './EditarPedido.css';

export default function EditarPedido({ folio, onClose, onCambio }) {
  const [editor, setEditor] = useState(null);
  const [recarga, setRecarga] = useState(0);
  const [cargando, setCargando] = useState(true);
  const [guardando, setGuardando] = useState(false);
  const [error, setError] = useState('');
  const [mensaje, setMensaje] = useState('');
  const [idVariante, setIdVariante] = useState('');
  const [cantidad, setCantidad] = useState('1');
  const [anulando, setAnulando] = useState(null);

  useEffect(() => {
    let vigente = true;
    consultarEdicionPedido(folio)
      .then(datos => { if (vigente) setEditor(datos); })
      .catch(fallo => { if (vigente) setError(fallo.message); })
      .finally(() => { if (vigente) setCargando(false); });
    return () => { vigente = false; };
  }, [folio, recarga]);

  function actualizar() {
    setCargando(true);
    setError('');
    setAnulando(null);
    setIdVariante('');
    setCantidad('1');
    setRecarga(valor => valor + 1);
  }

  async function guardar(operacion, aviso) {
    if (cargando || guardando) return;
    setGuardando(true);
    setError('');
    setMensaje('');
    try {
      await operacion();
      setMensaje(aviso);
      actualizar();
      onCambio();
    } catch (fallo) {
      setError(fallo.message);
    } finally {
      setGuardando(false);
    }
  }

  const pedido = editor?.pedido;
  const productos = editor?.catalogo.flatMap(categoria => categoria.productos) ?? [];
  const variante = productos.flatMap(producto => producto.variantes)
    .find(item => String(item.id) === idVariante && !item.agotado);
  const ocupado = guardando || cargando;

  return <Modal className="editar-pedido" label={`Detalle del pedido ${folio}`} onClose={() => { if (!guardando) onClose(); }}>
    <header className="editar-pedido-encabezado">
      <h2>Pedido {folio}</h2>
      {pedido && <span className="editar-pedido-estado">{pedido.estado}</span>}
    </header>
    {cargando && <p role="status">Consultando el pedido…</p>}
    {error && <p role="alert" className="checkout-error">{error}</p>}
    {mensaje && <p role="status">{mensaje}</p>}
    {error && <button type="button" className="button outline" disabled={ocupado} onClick={actualizar}>Actualizar detalle</button>}
    {pedido && <>
      <ul className="editar-pedido-articulos" aria-label="Artículos del pedido">
        {pedido.articulos.map(articulo => <li key={articulo.id}>
          <div>
            <strong>{articulo.cantidad} × {articulo.nombre}</strong>
            {articulo.variante && <small>{articulo.variante}</small>}
            {articulo.indicaciones && <small>{articulo.indicaciones}</small>}
          </div>
          <div className="editar-pedido-articulo-importe">
            <span>{money(articulo.subtotal)}</span>
            {pedido.editable && pedido.articulos.length > 1 && <button type="button" className="button outline editar-pedido-anular"
              aria-label={`Anular ${articulo.nombre}${articulo.variante ? `, ${articulo.variante}` : ''}`}
              disabled={ocupado} onClick={() => setAnulando(articulo)}>Anular</button>}
          </div>
        </li>)}
      </ul>
      <dl className="editar-pedido-importes">
        <div><dt>Subtotal</dt><dd>{money(pedido.subtotal)}</dd></div>
        <div><dt>Envío</dt><dd>{money(pedido.costoEnvio)}</dd></div>
        <div className="editar-pedido-total"><dt>Total</dt><dd>{money(pedido.total)}</dd></div>
      </dl>
      {pedido.editable && (anulando ? <form className="editar-pedido-formulario" onSubmit={event => {
        event.preventDefault();
        const motivo = new FormData(event.currentTarget).get('motivo').trim();
        if (motivo) void guardar(
          () => anularArticuloPedido(folio, { revision: pedido.revision, idDetalle: anulando.id, motivo }),
          'Renglón anulado.');
      }}>
        <h3>Anular {anulando.cantidad} × {anulando.nombre}</h3>
        <label className="settings-field">Motivo<textarea name="motivo" required maxLength={1000} disabled={ocupado}/></label>
        <div className="editar-pedido-acciones">
          <button type="button" className="button outline" disabled={ocupado} onClick={() => setAnulando(null)}>Volver</button>
          <button type="submit" className="button primary" disabled={ocupado}>Confirmar anulación</button>
        </div>
      </form> : <form className="editar-pedido-formulario" onSubmit={event => {
        event.preventDefault();
        if (variante) void guardar(
          () => agregarArticuloPedido(folio, { revision: pedido.revision, idVariante: variante.id,
            cantidad: Number(cantidad), precioEsperado: variante.precio }),
          'Artículo agregado.');
      }}>
        <h3>Agregar artículo</h3>
        <div className="editar-pedido-campos">
          <label className="settings-field">Artículo
            <span className="editar-pedido-selector">
              <select required value={idVariante} disabled={ocupado} onChange={event => setIdVariante(event.target.value)}>
                <option value="">Selecciona un artículo</option>
                {editor.catalogo.map(categoria => <optgroup key={categoria.id} label={categoria.nombre}>
                  {categoria.productos.flatMap(producto => producto.variantes.map(item =>
                    <option key={item.id} value={item.id} disabled={item.agotado}>
                      {producto.nombre}{item.nombre ? ` · ${item.nombre}` : ''} · {money(item.precio)}{item.agotado ? ' · Agotado' : ''}
                    </option>))}
                </optgroup>)}
              </select>
            </span>
          </label>
          <label className="settings-field">Cantidad
            <input type="number" min={1} max={20} step={1} required disabled={ocupado} value={cantidad}
              onChange={event => setCantidad(event.target.value)}/>
          </label>
        </div>
        <div className="editar-pedido-acciones">
          <button type="submit" className="button primary" disabled={ocupado || !variante}>Agregar al pedido</button>
        </div>
      </form>)}
      <section className="editar-pedido-historial">
        <h3>Historial de modificaciones</h3>
        {pedido.cambios.length === 0 ? <p>El pedido todavía no tiene modificaciones.</p> : <ul>
          {pedido.cambios.map(cambio => <li key={cambio.id}>
            <strong>{cambio.descripcion}</strong>
            <small>{new Date(cambio.fecha).toLocaleString('es-MX')} · {cambio.operador}</small>
          </li>)}
        </ul>}
      </section>
    </>}
  </Modal>;
}
