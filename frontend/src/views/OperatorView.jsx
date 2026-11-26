import { useCallback, useEffect, useRef, useState } from 'react';
import { obtenerPedidosOperador, cambiarEstadoPedido } from '../services/pedidos.js';
import { money } from '../utils/formato.js';
import Modal from '../components/Modal.jsx';

export default function OperatorView() {
  const [pedidos, setPedidos] = useState([]);
  const [error, setError] = useState('');
  const [cargando, setCargando] = useState(true);
  const [actualizando, setActualizando] = useState(null);
  const [cancelando, setCancelando] = useState(null);
  const ocupado = useRef(false);
  const version = useRef(0);
  const activo = useRef(true);
  const cargar = useCallback(async (automatica = false) => {
    if (ocupado.current) return;
    const consulta = ++version.current;
    if (!automatica) setCargando(true);
    try {
      const datos = await obtenerPedidosOperador(automatica);
      if (activo.current && consulta === version.current) { setPedidos(datos); setError(''); }
    } catch (fallo) { if (activo.current && consulta === version.current) setError(fallo.message); }
    finally { if (activo.current && consulta === version.current) setCargando(false); }
  }, []);
  useEffect(() => {
    activo.current = true;
    void cargar();
    const timer = setInterval(() => { if (document.visibilityState === 'visible') void cargar(true); }, 10000);
    return () => { activo.current = false; clearInterval(timer); };
  }, [cargar]);

  async function cambiar(folio, estado, motivo) {
    if (ocupado.current) return;
    ocupado.current = true;
    version.current++;
    setActualizando(folio);
    setError('');
    try {
      await cambiarEstadoPedido(folio, estado, motivo);
      setCancelando(null);
      ocupado.current = false;
      await cargar();
    } catch (fallo) { setError(fallo.message); }
    finally { ocupado.current = false; setActualizando(null); setCargando(false); }
  }

  return <main className="container page">
    <div className="page-heading"><span className="eyebrow orange">RESTAURANTE</span><h1>Operación de pedidos</h1>
      <p>Pedidos de tu restaurante. Primero aparecen los pendientes de atender.</p>
      <button className="button outline" disabled={cargando || !!actualizando} onClick={() => void cargar()}>Actualizar pedidos</button>
    </div>
    {error && <p className="checkout-error" role="alert">{error}</p>}
    {cargando && <p role="status">Consultando pedidos…</p>}
    {!cargando && !error && pedidos.length === 0 && <p>No hay pedidos registrados en tu restaurante.</p>}
    <div className="history-list">{pedidos.map(p => <article className="surface history-card" key={p.folio}>
      <div className="history-main"><h2>{p.folio}</h2><span className={`badge ${p.estado === 'Entregado' ? 'success' : p.estado === 'Cancelado' ? 'error' : 'warning'}`}>{p.estado}</span>
        <p>{p.cliente} · {p.tipoEntrega} · {new Date(p.fechaCreacion).toLocaleString('es-MX')}</p>
        <ul>{p.articulos.map((a, i) => <li key={i}>{a.cantidad} × {a.nombreProducto}{a.variante ? ` · ${a.variante}` : ''}</li>)}</ul>
        <strong>{money(p.total)}</strong>
      </div>
      <div className="history-actions">
        {p.estado === 'Recibido' && <button className="button primary" disabled={!!actualizando} onClick={() => void cambiar(p.folio, 'En preparación')}>Aceptar y preparar</button>}
        {p.estado === 'En preparación' && <button className="button primary" disabled={!!actualizando} onClick={() => void cambiar(p.folio, p.tipoEntrega === 'Domicilio' ? 'Enviado' : 'Listo para recoger')}>{p.tipoEntrega === 'Domicilio' ? 'Marcar Enviado' : 'Listo para recoger'}</button>}
        {['Enviado', 'Listo para recoger'].includes(p.estado) && <button className="button primary" disabled={!!actualizando} onClick={() => void cambiar(p.folio, 'Entregado')}>Marcar entregado</button>}
        {['Recibido', 'En preparación'].includes(p.estado) && <button className="button outline" disabled={!!actualizando} onClick={() => setCancelando(p.folio)}>Cancelar pedido</button>}
        {actualizando === p.folio && <span role="status">Guardando estado…</span>}
      </div>
    </article>)}</div>
    <p className="muted">Se muestran hasta 50 pedidos y se consultan cada 10 segundos con la pestaña visible.</p>
    {cancelando && <Modal label="Cancelar pedido" onClose={() => { if (!actualizando) setCancelando(null); }}>
      <h2>Cancelar {cancelando}</h2><form onSubmit={event => {
        event.preventDefault();
        const motivo = new FormData(event.currentTarget).get('motivo').trim();
        if (motivo) void cambiar(cancelando, 'Cancelado', motivo);
      }}><label>Motivo<textarea name="motivo" required maxLength={1000} disabled={!!actualizando}/></label>
        <button className="button primary full" disabled={!!actualizando}>Confirmar cancelación</button>
      </form>{error && <p role="alert">{error}</p>}
    </Modal>}
  </main>;
}
