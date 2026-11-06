"""Comprueba permisos contra la API local de LoyOrder, sin dependencias externas."""
import getpass
import json
import sys
from urllib.request import Request, urlopen
from urllib.error import HTTPError, URLError

BASE = "http://localhost:5080/api/v1"

def peticion(ruta, metodo="GET", datos=None, token=None):
    headers = {}
    if datos is not None:
        headers["Content-Type"] = "application/json"
    if token:
        headers["Authorization"] = "Bearer " + token
    request = Request(BASE + ruta, data=json.dumps(datos).encode("utf-8") if datos is not None else None,
                      headers=headers, method=metodo)
    try:
        with urlopen(request, timeout=10) as response:
            return response.status, response.read()
    except HTTPError as error:
        return error.code, error.read()

def main():
    correctas = 0
    fallidas = 0
    status, _ = peticion("/categorias?idRestaurante=1")
    print(f"Sin sesion: {status} (esperado 401)")
    correctas += status == 401
    fallidas += status != 401
    cuentas = [("CLIENTE", "", 200), ("OPERADOR", "operador@example.com", 403),
               ("ADMINISTRADOR", "administrador@example.com", 403)]
    for rol, sugerido, esperado in cuentas:
        print(f"\nCuenta de prueba: {rol}")
        correo = input(f"Correo{f' [{sugerido}]' if sugerido else ''}: ").strip() or sugerido
        clave = getpass.getpass("Contrasena (no se muestra): ")
        status, cuerpo = peticion("/auth/login", "POST", {"correo": correo, "contrasena": clave})
        del clave
        if status != 200:
            print(f"NO COMPROBADO: el inicio de sesion respondio {status}. Revisa correo, contrasena y cuenta activa.")
            fallidas += 1
            continue
        sesion = json.loads(cuerpo)
        token = sesion["accessToken"]
        try:
            if sesion["usuario"]["rol"] != rol:
                print("FALLO: la cuenta no tiene el rol esperado.")
                fallidas += 1
                continue
            status, _ = peticion("/categorias?idRestaurante=1", token=token)
            ok = status == esperado
            print(f"{'OK' if ok else 'FALLO'}: categorias devuelve {status} (esperado {esperado})")
            correctas += ok
            fallidas += not ok
        finally:
            cierre, _ = peticion("/auth/logout", "POST", token=token)
            if cierre != 204:
                print(f"AVISO: cierre de la sesion de prueba respondio {cierre}.")
                fallidas += 1
            del token
            del sesion
    print(f"\n{correctas}/4 comprobaciones de permisos aprobadas.")
    return 1 if fallidas else 0

if __name__ == "__main__":
    try:
        sys.exit(main())
    except URLError:
        print("No se pudo conectar con la API. Dejala encendida en http://localhost:5080.")
        sys.exit(1)
    except (KeyboardInterrupt, EOFError):
        print("\nPrueba cancelada.")
        sys.exit(1)
