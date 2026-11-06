import { useEffect } from 'react';
import { Navigate, Route, Routes, useLocation } from 'react-router';
import { useAuth } from './state/AuthContext.js';
import { rutaInicial } from './utils/auth-routing.js';
import ProtectedRoute, { PublicOnly } from './components/ProtectedRoute.jsx';
import AccessView from './views/AccessView.jsx';
import OperatorView from './views/OperatorView.jsx';
import Navbar from './components/Navbar.jsx';
import CartDrawer from './components/CartDrawer.jsx';
import AdminConfig from './components/AdminConfig.jsx';
import MenuView from './views/MenuView.jsx';
import TrackingView from './views/TrackingView.jsx';
import HistoryView from './views/HistoryView.jsx';
import AdminDashboard from './views/AdminDashboard.jsx';
import { useStore } from './state/StoreContext.jsx';

function Toast() {
  const { state, dispatch } = useStore();
  useEffect(() => {
    if (!state.toast) return;
    const timer = setTimeout(() => dispatch({ type: 'CLEAR_TOAST' }), 3200);
    return () => clearTimeout(timer);
  }, [state.toast, dispatch]);
  return <div id="toast-root" aria-live="polite">{state.toast && <div className={`toast ${state.toast.error ? 'error' : ''}`} role="status">{state.toast.message}</div>}</div>;
}

export default function App() {
  const { state, dispatch } = useStore();
  const { usuario } = useAuth();
  const { pathname } = useLocation();
  const titulo = pathname === '/login' ? 'Iniciar sesión' : pathname === '/registro' ? 'Registro' : pathname === '/operacion' ? 'Operación' : pathname.startsWith('/admin') ? 'Administración' : 'Pedidos';
  const count = state.cart.reduce((sum, item) => sum + item.quantity, 0);
  useEffect(() => { document.title = `${titulo} · LoyOrder`; }, [titulo]);
  return <><Navbar/><Routes>
    <Route path="/" element={<Navigate replace to={rutaInicial(usuario)}/>}/>
    <Route path="/login" element={<PublicOnly><AccessView key="login" mode="login"/></PublicOnly>}/>
    <Route path="/registro" element={<PublicOnly><AccessView key="registro" mode="registro"/></PublicOnly>}/>
    <Route path="/operacion" element={<ProtectedRoute roles={['OPERADOR']}><OperatorView/></ProtectedRoute>}/>
    <Route path="/menu" element={<ProtectedRoute roles={['CLIENTE']}><MenuView/></ProtectedRoute>}/>
    <Route path="/seguimiento" element={<ProtectedRoute roles={['CLIENTE']}><TrackingView/></ProtectedRoute>}/>
    <Route path="/historial" element={<ProtectedRoute roles={['CLIENTE']}><HistoryView/></ProtectedRoute>}/>
    <Route path="/admin" element={<ProtectedRoute roles={['ADMINISTRADOR']}><AdminDashboard/></ProtectedRoute>}/>
    <Route path="/admin/configuracion" element={<ProtectedRoute roles={['ADMINISTRADOR']}><AdminConfig/></ProtectedRoute>}/>
    <Route path="*" element={<Navigate replace to={rutaInicial(usuario)}/>}/>
  </Routes><footer className="site-footer"><div className="container"><span className="logo"><span className="logo-mark">✳</span>Loy<span>Order</span></span><p>Hecho con sabor y cariño · Demo React © 2026</p></div></footer>{usuario?.rol === 'CLIENTE' && <button className="cart-fab" aria-label="Abrir carrito" onClick={() => dispatch({ type: 'OPEN_CART' })}>🛒{count > 0 && <b>{count}</b>}</button>}{usuario?.rol === 'CLIENTE' && <CartDrawer/>}<Toast/></>;
}
