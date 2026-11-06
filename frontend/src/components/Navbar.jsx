import { NavLink, useNavigate } from 'react-router';
import { useState } from 'react';
import { useStore } from '../state/StoreContext.jsx';
import { useAuth } from '../state/AuthContext.js';
import { rutaInicial } from '../utils/auth-routing.js';

export default function Navbar() {
  const { state, dispatch } = useStore();
  const { usuario, cerrarSesion } = useAuth();
  const [mobileMenu, setMobileMenu] = useState(false);
  const [cerrando, setCerrando] = useState(false);
  const navigate = useNavigate();
  const cliente = usuario?.rol === 'CLIENTE';
  const links = !usuario ? [['/login', 'Iniciar sesión'], ['/registro', 'Crear cuenta']]
    : usuario.rol === 'ADMINISTRADOR' ? [['/admin', 'Dashboard'], ['/admin/configuracion', 'Configuración']]
    : cliente ? [['/menu', 'Explorar menú'], ['/seguimiento', 'Mi pedido'], ['/historial', 'Historial']]
    : [['/operacion', 'Operación']];
  const count = state.cart.reduce((sum, item) => sum + item.quantity, 0);
  async function salir() {
    setCerrando(true);
    try {
      await cerrarSesion();
      setMobileMenu(false);
      navigate('/login', { replace: true });
    } catch (error) { dispatch({ type: 'NOTIFY', message: error.message, error: true }); }
    finally { setCerrando(false); }
  }
  return <header className="site-header"><div className="header-inner">
    <NavLink to={rutaInicial(usuario)} className="logo"><span className="logo-mark">✳</span>Loy<span>Order</span></NavLink>
    <nav className={`main-nav ${mobileMenu ? 'open' : ''}`} aria-label="Navegación principal">{links.map(([to, label]) => <NavLink key={to} end to={to} onClick={() => setMobileMenu(false)} className={({ isActive }) => isActive ? 'active' : ''}>{label}</NavLink>)}</nav>
    <div className="header-actions">{usuario && <button className="button subtle" onClick={salir} disabled={cerrando}>{cerrando ? 'Cerrando…' : 'Cerrar sesión'}</button>}
      {cliente && <button className="cart-top" onClick={() => dispatch({ type: 'OPEN_CART' })}>🛒 <span>Carrito</span><b>{count}</b></button>}
      <button className="menu-toggle" onClick={() => setMobileMenu(!mobileMenu)} aria-label={mobileMenu ? 'Cerrar menú' : 'Abrir menú'} aria-expanded={mobileMenu}>☰</button>
    </div>
  </div></header>;
}
