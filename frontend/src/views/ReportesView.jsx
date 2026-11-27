import { useState } from 'react';
import { Link } from 'react-router';
import { consultarReporte, descargarReporte } from '../services/reportes.js';
import { money } from '../utils/formato.js';
import './ReportesView.css';

const filasPorPagina = 25;
const fechaLocal = valor => valor.split('-').reverse().join('/');
const fechaHora = valor => `${fechaLocal(valor.slice(0, 10))} ${valor.slice(11, 16)}`;
const filtrosIniciales = () => {
  const hoy = new Intl.DateTimeFormat('sv-SE', { timeZone: 'America/Mexico_City' }).format(new Date());
  return { tipo: 'ventas', desde: `${hoy.slice(0, 7)}-01`, hasta: hoy };
};

export default function ReportesView() {
  const [filtros, setFiltros] = useState(filtrosIniciales);
  const [resultado, setResultado] = useState(null);
  const [pagina, setPagina] = useState(0);
  const [ocupado, setOcupado] = useState(false);
  const [error, setError] = useState('');

  async function consultar(evento) {
    evento.preventDefault();
    setOcupado(true);
    setError('');
    try {
      const reporte = await consultarReporte(filtros);
      setResultado({ filtros, reporte });
      setPagina(0);
    } catch (fallo) { setError(fallo.message); }
    finally { setOcupado(false); }
  }

  async function descargar(formato) {
    if (!resultado || ocupado) return;
    setOcupado(true);
    setError('');
    try {
      const { tipo, desde, hasta } = resultado.filtros;
      const archivo = await descargarReporte(resultado.filtros, formato);
      const url = URL.createObjectURL(archivo);
      const enlace = document.createElement('a');
      enlace.href = url;
      enlace.download = `loyorder-${tipo}-${desde.replaceAll('-', '')}-${hasta.replaceAll('-', '')}.${formato}`;
      document.body.append(enlace);
      enlace.click();
      enlace.remove();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (fallo) { setError(fallo.message); }
    finally { setOcupado(false); }
  }

  const reporte = resultado?.reporte;
  const ventas = resultado?.filtros.tipo === 'ventas';
  const registros = reporte?.registros ?? [];
  const inicio = pagina * filasPorPagina;

  return <main className="container page reportes-page">
    <div className="reportes-encabezado">
      <h1>Reportes</h1>
      <Link className="button outline" to="/admin">Volver a administración</Link>
    </div>
    <form className="surface reportes-filtros" onSubmit={consultar}>
      <label className="settings-field">Tipo de reporte
        <select value={filtros.tipo} disabled={ocupado} onChange={e => setFiltros({ ...filtros, tipo: e.target.value })}>
          <option value="ventas">Ventas entregadas</option>
          <option value="pedidos">Pedidos</option>
        </select>
      </label>
      <label className="settings-field">Desde
        <input type="date" required max={filtros.hasta || undefined} value={filtros.desde}
          disabled={ocupado} onChange={e => setFiltros({ ...filtros, desde: e.target.value })}/>
      </label>
      <label className="settings-field">Hasta
        <input type="date" required min={filtros.desde || undefined} value={filtros.hasta}
          disabled={ocupado} onChange={e => setFiltros({ ...filtros, hasta: e.target.value })}/>
      </label>
      <button className="button primary" disabled={ocupado}>{ocupado ? 'Procesando…' : 'Consultar'}</button>
    </form>
    {error && <p role="alert" className="checkout-error">{error}</p>}
    {reporte && <section className="surface table-section reportes-resultado" aria-label="Resultados del reporte">
      <div className="reportes-cabecera">
        <div>
          <h2>{ventas ? 'Ventas entregadas' : 'Pedidos'}</h2>
          <p className="muted">{fechaLocal(reporte.desde)} – {fechaLocal(reporte.hasta)} · {ventas ? 'Por fecha de entrega' : 'Por fecha de creación'}</p>
        </div>
        <div className="reportes-acciones">
          <button className="button outline" disabled={ocupado} onClick={() => void descargar('pdf')}>Descargar PDF</button>
          <button className="button outline" disabled={ocupado} onClick={() => void descargar('xlsx')}>Descargar Excel</button>
        </div>
      </div>
      <dl className="reportes-resumen">
        <div><dt>Registros</dt><dd>{registros.length}</dd></div>
        <div><dt>{ventas ? 'Importe de ventas entregadas' : 'Importe de los pedidos'}</dt><dd>{money(reporte.importe)}</dd></div>
        {reporte.estados.map(estado => <div key={estado.estado}><dt>{estado.estado}</dt><dd>{estado.cantidad}</dd></div>)}
      </dl>
      {registros.length === 0 ? <p className="reportes-vacio">No hay registros en este periodo.</p> : <>
        <div className="table-scroll">
          <table>
            <thead><tr><th scope="col">Folio</th><th scope="col">Fecha</th><th scope="col">Modalidad</th>
              <th scope="col">{ventas ? 'Estado en Loyverse' : 'Estado'}</th><th scope="col">Subtotal</th><th scope="col">Envío</th>
              <th scope="col">Total</th>{ventas && <th scope="col">Recibo</th>}</tr></thead>
            <tbody>{registros.slice(inicio, inicio + filasPorPagina).map(r => <tr key={r.folio}>
              <th scope="row">{r.folio}</th><td>{fechaHora(r.fecha)}</td><td>{r.modalidad}</td><td>{r.estado}</td>
              <td>{money(r.subtotal)}</td><td>{money(r.envio)}</td><td>{money(r.total)}</td>{ventas && <td>{r.recibo ?? '—'}</td>}
            </tr>)}</tbody>
          </table>
        </div>
        {registros.length > filasPorPagina && <nav className="reportes-paginacion" aria-label="Paginación del reporte">
          <span>{inicio + 1}–{Math.min(inicio + filasPorPagina, registros.length)} de {registros.length}</span>
          <button className="button outline" onClick={() => setPagina(pagina - 1)} disabled={pagina === 0}>Anterior</button>
          <button className="button outline" onClick={() => setPagina(pagina + 1)} disabled={inicio + filasPorPagina >= registros.length}>Siguiente</button>
        </nav>}
      </>}
    </section>}
  </main>;
}
