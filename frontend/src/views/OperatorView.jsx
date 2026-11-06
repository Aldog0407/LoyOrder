import { useAuth } from '../state/AuthContext.js';
export default function OperatorView() {
  const { usuario } = useAuth();
  return <main className="container auth-wrap"><section className="auth-card"><span className="eyebrow orange">LOYORDER</span><h1>Acceso de operador</h1><p>Sesión iniciada como {usuario.nombre}.</p><p>Las funciones de operación de pedidos se integrarán en sus actividades correspondientes.</p></section></main>;
}
