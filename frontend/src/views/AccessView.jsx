import { useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { registrar } from '../auth.js';
import { cumpleContrasena, mensajeContrasena } from '../politica-contrasena.js';
import { useAuth } from '../state/AuthContext.js';
import { useStore } from '../state/StoreContext.jsx';
import { rutaInicial } from '../utils/auth-routing.js';

export default function AccessView({ mode }) {
  const registro = mode === 'registro';
  const [error, setError] = useState('');
  const [enviando, setEnviando] = useState(false);
  const { iniciarSesion } = useAuth();
  const { dispatch } = useStore();
  const navigate = useNavigate();
  async function enviar(event) {
    event.preventDefault();
    if (enviando) return;
    const datos = Object.fromEntries(new FormData(event.currentTarget));
    setError('');
    if (registro && !cumpleContrasena(datos.contrasena)) { setError(mensajeContrasena); return; }
    if (registro && datos.contrasena !== datos.confirmacion) { setError('Las contraseñas no coinciden.'); return; }
    setEnviando(true);
    try {
      if (registro) {
        await registrar({ nombre: datos.nombre, correo: datos.correo.trim(), contrasena: datos.contrasena, telefono: datos.telefono.trim() || null });
        navigate('/login', { replace: true });
        dispatch({ type: 'NOTIFY', message: 'Cuenta creada. Ya puedes iniciar sesión.' });
      } else {
        const usuario = await iniciarSesion({ correo: datos.correo.trim(), contrasena: datos.contrasena });
        navigate(rutaInicial(usuario), { replace: true });
      }
    } catch (fallo) { setError(fallo.message); }
    finally { setEnviando(false); }
  }
  return <main className="container auth-wrap"><section className="auth-card" aria-labelledby="auth-title">
    <span className="eyebrow orange">BIENVENIDO A LOYORDER</span>
    <h1 id="auth-title">{registro ? 'Crea tu cuenta' : 'Iniciar sesión'}</h1>
    <p>{registro ? 'Regístrate para realizar tus pedidos.' : 'Accede con tu correo y contraseña.'}</p>
    <form onSubmit={enviar} aria-busy={enviando}>
      <fieldset disabled={enviando}>
        {registro && <><label>Nombre<input name="nombre" required maxLength={100} autoComplete="name"/></label>
          <label>Teléfono (opcional)<input name="telefono" type="tel" maxLength={20} autoComplete="tel"/></label></>}
        <label>Correo electrónico<input name="correo" type="email" required maxLength={150} autoComplete="email"/></label>
        <label>Contraseña<input name="contrasena" type="password" required minLength={registro ? 8 : undefined} maxLength={128} autoComplete={registro ? 'new-password' : 'current-password'} aria-describedby={registro ? 'password-help' : undefined}/></label>
        {registro && <><label>Confirmar contraseña<input name="confirmacion" type="password" required maxLength={128} autoComplete="new-password"/></label>
          <small id="password-help">Entre 8 y 128 caracteres y al menos un símbolo, como !, # o $.</small></>}
        <p id="auth-error" role="alert" aria-live="polite">{error}</p>
        <button className="button primary full" type="submit">{enviando ? 'Procesando…' : registro ? 'Crear cuenta' : 'Iniciar sesión'}</button>
      </fieldset>
    </form>
    <Link className="button subtle full" to={registro ? '/login' : '/registro'}>{registro ? 'Ya tengo cuenta' : 'Crear una cuenta de cliente'}</Link>
  </section></main>;
}
