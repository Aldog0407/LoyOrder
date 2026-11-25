import { useEffect, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router';
import { authApi } from '../services/authApi.js';
import { cumpleContrasena, mensajeContrasena } from '../politica-contrasena.js';

export default function EmailAccessView({ mode }) {
  const location = useLocation();
  const navigate = useNavigate();
  const verificar = mode === 'verificar';
  const restablecer = mode === 'restablecer';
  const [idDesafio, setIdDesafio] = useState(location.state?.idDesafio ?? '');
  const [token] = useState(() => new URLSearchParams(location.search).get('token') ?? '');
  const tokenValido = /^[0-9a-f]{32}\.[0-9a-f]{64}$/i.test(token);
  const [error, setError] = useState('');
  const [mensaje, setMensaje] = useState('');
  const [enviando, setEnviando] = useState(false);
  const [completo, setCompleto] = useState(false);
  const [esperaHasta, setEsperaHasta] = useState(() => verificar && idDesafio ? Date.now() + 60000 : 0);
  const [ahora, setAhora] = useState(Date.now);
  const espera = Math.max(0, Math.ceil((esperaHasta - ahora) / 1000));

  useEffect(() => {
    if (esperaHasta <= Date.now()) return;
    const timer = setInterval(() => setAhora(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [esperaHasta]);

  useEffect(() => {
    if (restablecer && location.search) navigate(location.pathname, { replace: true });
  }, [restablecer, location.search, location.pathname, navigate]);

  function mostrarError(fallo) {
    setError(fallo.status === 404 ? 'El servicio de correo aún no está habilitado. Intenta más tarde.' : fallo.message);
    if (fallo.status === 429) {
      setAhora(Date.now());
      setEsperaHasta(Date.now() + (fallo.retryAfter ?? 60) * 1000);
    }
  }

  async function enviar(event) {
    event.preventDefault();
    if (enviando || completo || espera > 0 && !verificar) return;
    const datos = Object.fromEntries(new FormData(event.currentTarget));
    setError('');
    if (restablecer && !cumpleContrasena(datos.nuevaContrasena)) { setError(mensajeContrasena); return; }
    if (restablecer && datos.nuevaContrasena !== datos.confirmacion) { setError('Las contraseñas no coinciden.'); return; }
    setEnviando(true);
    try {
      if (verificar) {
        await authApi.confirmEmail({ idDesafio, codigo: datos.codigo });
        setMensaje('Correo verificado. Ya puedes iniciar sesión.');
      } else if (restablecer) {
        await authApi.resetPassword({ token, nuevaContrasena: datos.nuevaContrasena });
        setMensaje('Contraseña actualizada. Inicia sesión con tu nueva contraseña.');
      } else {
        await authApi.requestPassword({ correo: datos.correo.trim() });
        setMensaje('Si hay una cuenta asociada, recibirás un correo con las instrucciones. Revisa también la carpeta de spam.');
      }
      setCompleto(true);
    } catch (fallo) { mostrarError(fallo); }
    finally { setEnviando(false); }
  }

  async function reenviar() {
    if (enviando || espera > 0) return;
    setError('');
    setEnviando(true);
    try {
      const respuesta = await authApi.resendEmail({ idDesafio });
      setIdDesafio(respuesta.idDesafio);
      navigate(location.pathname, { replace: true, state: { idDesafio: respuesta.idDesafio } });
      setAhora(Date.now());
      setEsperaHasta(Date.now() + 60000);
      setMensaje('Solicitamos un nuevo código. Usa el del correo más reciente.');
    } catch (fallo) { mostrarError(fallo); }
    finally { setEnviando(false); }
  }

  const titulo = verificar ? 'Verifica tu correo' : restablecer ? 'Elige una nueva contraseña' : 'Recupera tu contraseña';
  const disponible = verificar ? Boolean(idDesafio) : !restablecer || tokenValido;
  return <main className="container auth-wrap"><section className="auth-card" aria-labelledby="email-title">
    <span className="eyebrow orange">TU CUENTA LOYORDER</span><h1 id="email-title">{titulo}</h1>
    {verificar && !completo && <p>Escribe el código de seis dígitos que enviamos a tu correo. Vence en 10 minutos.</p>}
    {!verificar && !restablecer && !completo && <p>Escribe el correo de tu cuenta para solicitar un enlace de recuperación.</p>}
    {!disponible && <p role="alert">{verificar ? 'Inicia sesión con tu correo y contraseña para solicitar otro código.' : 'El enlace está incompleto. Solicita uno nuevo para recuperar tu contraseña.'}</p>}
    {disponible && !completo && <form onSubmit={enviar} aria-busy={enviando}>
      <fieldset disabled={enviando}>
        {verificar ? <label>Código de verificación<input name="codigo" required inputMode="numeric" autoComplete="one-time-code" pattern="[0-9]{6}" minLength={6} maxLength={6}/></label> : restablecer ? <>
          <label>Nueva contraseña<input name="nuevaContrasena" type="password" required minLength={8} maxLength={128} autoComplete="new-password" aria-describedby="reset-password-help"/></label>
          <label>Confirmar contraseña<input name="confirmacion" type="password" required maxLength={128} autoComplete="new-password"/></label>
          <small id="reset-password-help">{mensajeContrasena}</small>
        </> : <label>Correo electrónico<input name="correo" type="email" required maxLength={150} autoComplete="email"/></label>}
        <button className="button primary full" type="submit" disabled={enviando || !verificar && espera > 0}>{enviando ? 'Procesando…' : verificar ? 'Verificar correo' : restablecer ? 'Guardar contraseña' : 'Enviar enlace'}</button>
      </fieldset>
    </form>}
    {error && <p role="alert">{error}</p>}
    {mensaje && <p role="status">{mensaje}</p>}
    {verificar && disponible && !completo && <button className="button subtle full" disabled={enviando || espera > 0} onClick={reenviar}>{espera > 0 ? `Reenviar en ${espera} s` : 'Reenviar código'}</button>}
    {!verificar && restablecer && !completo && <Link className="text-button" to="/recuperar-contrasena">Solicitar otro enlace</Link>}
    {!completo && espera > 0 && !verificar && <p role="status">Puedes volver a intentar en {espera} segundos.</p>}
    <Link className="button subtle full" to="/login">Volver a iniciar sesión</Link>
  </section></main>;
}
