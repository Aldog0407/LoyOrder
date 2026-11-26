"""Ejecuta las suites existentes y mide cobertura sin acceder a Loyverse ni enviar correo."""
from __future__ import annotations
import argparse
import collections
import datetime as dt
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")


def ejecutar(args: list[str], cwd: Path, log: Path) -> str:
    with log.open("w", encoding="utf-8") as destino:
        proceso = subprocess.run(args, cwd=cwd, stdout=destino, stderr=subprocess.STDOUT)
    texto = log.read_text(encoding="utf-8-sig", errors="replace")
    if proceso.returncode:
        raise RuntimeError(f"Falló {args[0]}. Código {proceso.returncode}. Consulta {log}\n{texto[-2000:]}")
    return texto


def leer_cobertura(archivo: Path) -> dict:
    raiz = ET.parse(archivo).getroot()
    modulos = []
    for paquete in raiz.findall("./packages/package"):
        hay_ramas = any(linea.get("branch", "").lower() == "true" for linea in paquete.findall("./classes/class/lines/line"))
        modulos.append({"modulo": paquete.get("name"),
                        "lineas_pct": float(paquete.get("line-rate", "0")) * 100,
                        "ramas_pct": float(paquete.get("branch-rate", "0")) * 100 if hay_ramas else None})
    return {"lineas_cubiertas": int(raiz.get("lines-covered", "0")),
            "lineas_medidas": int(raiz.get("lines-valid", "0")),
            "lineas_pct": float(raiz.get("line-rate", "0")) * 100,
            "ramas_cubiertas": int(raiz.get("branches-covered", "0")),
            "ramas_medidas": int(raiz.get("branches-valid", "0")),
            "ramas_pct": float(raiz.get("branch-rate", "0")) * 100,
            "modulos": modulos}


def leer_lcov(archivo: Path, fuente_frontend: Path) -> dict:
    # auth.js se importa con query strings para aislar sesiones de distintas pruebas.
    # V8 produce varios registros SF para el mismo archivo. Se unen por línea,
    # contando cada línea fuente una sola vez y cubierta si alguna instancia la ejecutó.
    archivos: dict[str, dict[int, int]] = collections.defaultdict(dict)
    actual = None
    registros = 0
    for linea in archivo.read_text(encoding="utf-8-sig").splitlines():
        if linea.startswith("SF:"):
            actual = linea[3:].replace("\\", "/").split("?", 1)[0]
            if Path(actual).is_absolute():
                actual = Path(actual).relative_to(fuente_frontend).as_posix()
            if not actual.startswith("src/") or not actual.endswith(".js"):
                raise ValueError(f"Archivo fuera del alcance esperado de JavaScript: {actual}")
            registros += 1
        elif linea.startswith("DA:") and actual:
            numero, hits = linea[3:].split(",")[:2]
            punto = int(numero)
            archivos[actual][punto] = archivos[actual].get(punto, 0) + int(hits)
    if not archivos:
        raise ValueError("LCOV vacío; no se puede declarar cobertura del frontend.")
    resumen = [{"archivo": nombre, "lineas_cubiertas": sum(h > 0 for h in lineas.values()),
                "lineas_medidas": len(lineas)} for nombre, lineas in sorted(archivos.items())]
    cubiertas = sum(a["lineas_cubiertas"] for a in resumen)
    medidas = sum(a["lineas_medidas"] for a in resumen)
    return {"lineas_cubiertas": cubiertas, "lineas_medidas": medidas,
            "lineas_pct": 100 * cubiertas / medidas, "archivos": resumen,
            "archivos_js_existentes": len(list((fuente_frontend / "src").rglob("*.js"))),
            "registros_lcov": registros}


def reporte_md(datos: dict) -> str:
    unidad = datos["backend_unitario"]
    completo = datos["backend_combinado"]
    front = datos["frontend"]
    filas = ["# Reporte de pruebas y cobertura · LoyOrder", "",
             f"Medición: {datos['fecha']}. Código fuente identificado por `{datos['revision']}`.", "",
             "## Ejecución", "", "| Suite | Tipo | Aprobadas |", "| --- | --- | ---: |"]
    for prueba in datos["suites"]:
        filas.append(f"| {prueba['nombre']} | {prueba['tipo']} | {prueba['aprobadas']} |")
    filas += ["", "Los grupos PostgreSQL contienen varias comprobaciones; no se suman como si tuvieran el mismo tamaño que una prueba unitaria.",
              "Las pruebas automatizadas usan dobles de Loyverse y no envían correos. PostgreSQL usa la base temporal indicada por la suite.", "",
              "## Cobertura", "", "| Alcance | Líneas cubiertas / medidas | Líneas | Ramas |",
              "| --- | ---: | ---: | ---: |",
              f"| Backend, solo suites unitarias | {unidad['lineas_cubiertas']} / {unidad['lineas_medidas']} | {unidad['lineas_pct']:.2f}% | {unidad['ramas_pct']:.2f}% |",
              f"| Backend, unitarias e integración{' HTTP y PostgreSQL' if datos['postgres_incluido'] else ' HTTP'} | {completo['lineas_cubiertas']} / {completo['lineas_medidas']} | {completo['lineas_pct']:.2f}% | {completo['ramas_pct']:.2f}% |",
              f"| Frontend, módulos JavaScript cargados por las pruebas | {front['lineas_cubiertas']} / {front['lineas_medidas']} | {front['lineas_pct']:.2f}% | No consolidada |", "",
              "El alcance unitario del backend incluye Application, Domain y el cliente/adaptador Loyverse. La medición combinada añade API y Persistence.",
              "Los porcentajes de backend se conservan como los informa Coverlet en Cobertura y no son un promedio simple de módulos.",
              f"El frontend mide {len(front['archivos'])} de {front['archivos_js_existentes']} archivos .js presentes. No incluye los componentes .jsx ni representa cobertura de todas las pantallas React.",
              "Los registros LCOV duplicados del mismo archivo se unen por número de línea; auth.js se recarga con query strings en distintas pruebas. No se consolida la cobertura de ramas de esas instancias.", "",
              "## Backend por módulo, ejecución combinada", "", "| Módulo | Líneas | Ramas |", "| --- | ---: | ---: |"]
    for modulo in completo["modulos"]:
        ramas = "No aplica" if modulo["ramas_pct"] is None else f"{modulo['ramas_pct']:.2f}%"
        filas.append(f"| {modulo['modulo']} | {modulo['lineas_pct']:.2f}% | {ramas} |")
    filas += ["", "## Límites y siguientes pruebas", "",
              "- La cobertura mide código ejecutado, no demuestra que todos los requisitos estén terminados ni que todos los resultados sean correctos.",
              "- El transporte SMTP y los bucles de trabajadores tienen menor cobertura automatizada. La recepción de avisos y el recibo real se documentan por separado como pruebas manuales.",
              "- Las pruebas HTTP no son pruebas unitarias; se presentan separadas aunque también ayudan a medir código del backend.",
              "- La impresión ESC/POS sigue pendiente del módulo de Carlos. Un módulo todavía no implementado no puede aumentar el denominador de cobertura.",
              "- El recorrido de venta con envío requiere configurar una variante real para el costo de envío y comprobarlo en la cuenta de pruebas.", "",
              "## Repetir la medición", "",
              "Desde la raíz del proyecto, con .NET 10, Node.js y Python 3 instalados:", "", "```powershell",
              "python tests/generar_cobertura.py", "```", "",
              "Para incluir PostgreSQL, definir localmente LOYORDER_PRUEBAS_ADMIN y ejecutar:", "", "```powershell",
              "python tests/generar_cobertura.py --incluir-postgres", "```", "",
              "La suite PostgreSQL crea y elimina exclusivamente la base de pruebas loyorder_pruebas_sync; no debe existir allí información que se necesite conservar. Esta medición se ejecutó sobre un servidor PostgreSQL temporal separado del entorno de Aldo.",
              "Sin --incluir-postgres el reporte indica que esa suite no se ejecutó y no presenta su resultado como aprobado.",
              "El script restaura Coverlet desde el manifiesto, compila una vez y ejecuta las DLL sin recompilarlas mientras están instrumentadas. Produce JSON, Cobertura XML, LCOV, registros y este resumen en una carpeta nueva de TestResults/cobertura.", "",
              "Herramientas: Coverlet.Console 10.1.1 y cobertura nativa de Node.js. La unión de líneas de LCOV y las tasas de Cobertura se pueden revisar en los archivos de evidencia.", ""]
    return "\n".join(filas)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--raiz", type=Path, default=Path(__file__).resolve().parents[1], help="Raíz del proyecto; permite medir una copia aislada.")
    parser.add_argument("--salida", type=Path, help="Carpeta nueva para los resultados.")
    parser.add_argument("--incluir-postgres", action="store_true")
    parser.add_argument("--sin-compilar", action="store_true", help="Usar DLL Debug ya compiladas.")
    parser.add_argument("--coverlet", type=Path, help="Ejecutable de Coverlet ya instalado; por defecto restaura el manifiesto local.")
    args = parser.parse_args()
    raiz = args.raiz.resolve()
    if not (raiz / "LoyOrder.sln").is_file():
        parser.error("La raíz no contiene LoyOrder.sln.")
    if args.incluir_postgres and not os.environ.get("LOYORDER_PRUEBAS_ADMIN"):
        parser.error("Define LOYORDER_PRUEBAS_ADMIN localmente antes de incluir PostgreSQL.")
    fecha = dt.datetime.now().astimezone()
    salida = (args.salida or raiz / "TestResults/cobertura" / fecha.strftime("%Y%m%d-%H%M%S")).resolve()
    if salida.exists() and any(salida.iterdir()):
        parser.error("La salida contiene archivos. Elige una carpeta nueva para no mezclar mediciones.")
    salida.mkdir(parents=True, exist_ok=True)
    if args.coverlet:
        herramienta = [str(args.coverlet.resolve())]
    else:
        ejecutar(["dotnet", "tool", "restore"], raiz, salida / "herramientas.log")
        herramienta = ["dotnet", "tool", "run", "coverlet", "--"]
    if not args.sin_compilar:
        ejecutar(["dotnet", "build", "LoyOrder.sln", "-c", "Debug"], raiz, salida / "compilacion.log")
    revision = subprocess.run(["git", "rev-parse", "HEAD"], cwd=raiz, capture_output=True, text=True).stdout.strip()
    if not revision:
        revision = subprocess.run(["git", "rev-parse", "HEAD"], cwd=Path(__file__).resolve().parents[1], capture_output=True, text=True).stdout.strip() or "no disponible"
    pasos = [("unit-aplicacion", "tests/unit/LoyOrder.Application.Tests", "LoyOrder.Application.Tests", "Unitarias"),
             ("unit-backend", "tests/unit/LoyOrder.Infrastructure.Loyverse.Tests", "LoyOrder.Infrastructure.Loyverse.Tests", "Unitarias"),
             ("backend-api", "tests/integration/LoyOrder.Api.Tests", "LoyOrder.Api.Tests", "HTTP e integración")]
    if args.incluir_postgres:
        pasos.append(("backend-completo", "tests/integration/LoyOrder.Persistence.Tests", "LoyOrder.Persistence.Tests", "Grupos PostgreSQL"))
    anterior = None
    suites = []
    for nombre, carpeta, proyecto, tipo in pasos:
        dll = raiz / carpeta / "bin/Debug/net10.0" / (proyecto + ".dll")
        if not dll.is_file():
            raise FileNotFoundError(f"No existe {dll}; compila la solución antes de medir.")
        destino = salida / nombre
        destino.mkdir()
        comando = herramienta + [str(dll), "--target", "dotnet", "--targetargs", '"' + str(dll) + '"',
                    "--include", "[LoyOrder.*]*", "--exclude", "[*.Tests]*", "--exclude-by-file", "**/obj/**",
                    "--format", "json", "--format", "cobertura", "--output", str(destino) + os.sep]
        if anterior:
            comando += ["--merge-with", str(anterior)]
        texto = ejecutar(comando, raiz, salida / (nombre + ".log"))
        conteos = re.findall(r"(\d+)(?: de \d+)? pruebas aprobadas\.", texto)
        if not conteos:
            raise ValueError(f"No se encontró resultado de la suite {proyecto}.")
        suites.append({"nombre": proyecto, "tipo": tipo, "aprobadas": int(conteos[-1])})
        anterior = destino / "coverage.json"
        if not anterior.is_file():
            raise FileNotFoundError("Coverlet no generó su archivo de resultados.")
        print(f"{proyecto}: {conteos[-1]} aprobadas")
    frontend = raiz / "frontend"
    ejecutar(["node", "--experimental-test-coverage", "--test-coverage-include=src/**/*.js",
              "--test-reporter=tap", "--test-reporter-destination=" + str(salida / "frontend.log"),
              "--test-reporter=lcov", "--test-reporter-destination=" + str(salida / "frontend.lcov"), "--test"],
             frontend, salida / "frontend-ejecucion.log")
    tap = (salida / "frontend.log").read_text(encoding="utf-8-sig")
    aprobadas = re.search(r"^# pass (\d+)$", tap, re.MULTILINE)
    fallidas = re.search(r"^# fail (\d+)$", tap, re.MULTILINE)
    if not aprobadas or not fallidas or int(fallidas[1]) != 0:
        raise ValueError("El resultado del frontend no acredita pruebas aprobadas sin fallos.")
    suites.append({"nombre": "Frontend Node.js", "tipo": "Unitarias de JavaScript", "aprobadas": int(aprobadas[1])})
    datos = {"fecha": fecha.isoformat(timespec="seconds"), "revision": revision,
             "postgres_incluido": args.incluir_postgres, "suites": suites,
             "backend_unitario": leer_cobertura(salida / "unit-backend/coverage.cobertura.xml"),
             "backend_combinado": leer_cobertura(anterior.with_name("coverage.cobertura.xml")),
             "frontend": leer_lcov(salida / "frontend.lcov", frontend)}
    (salida / "resumen.json").write_text(json.dumps(datos, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (salida / "REPORTE.md").write_text(reporte_md(datos), encoding="utf-8")
    print(f"Reporte: {salida / 'REPORTE.md'}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (RuntimeError, ValueError, FileNotFoundError) as error:
        print(str(error), file=sys.stderr)
        raise SystemExit(1)
