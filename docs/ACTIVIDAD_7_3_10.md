# Reporte de pruebas y cobertura · LoyOrder

Medición: 2026-11-26T10:15:16-06:00. Código fuente identificado por `23a777f88a3b5e3a2f585ec537835d414719df9e`.

## Ejecución

| Suite | Tipo | Aprobadas |
| --- | --- | ---: |
| LoyOrder.Application.Tests | Unitarias | 32 |
| LoyOrder.Infrastructure.Loyverse.Tests | Unitarias | 32 |
| LoyOrder.Api.Tests | HTTP e integración | 127 |
| LoyOrder.Persistence.Tests | Grupos PostgreSQL | 17 |
| Frontend Node.js | Unitarias de JavaScript | 42 |

Los grupos PostgreSQL contienen varias comprobaciones; no se suman como si tuvieran el mismo tamaño que una prueba unitaria.
Las pruebas automatizadas usan dobles de Loyverse y no envían correos. PostgreSQL usa la base temporal indicada por la suite.

## Cobertura

| Alcance | Líneas cubiertas / medidas | Líneas | Ramas |
| --- | ---: | ---: | ---: |
| Backend, solo suites unitarias | 570 / 763 | 74.70% | 66.03% |
| Backend, unitarias e integración HTTP y PostgreSQL | 1694 / 2283 | 74.20% | 67.98% |
| Frontend, módulos JavaScript cargados por las pruebas | 499 / 516 | 96.71% | No consolidada |

El alcance unitario del backend incluye Application, Domain y el cliente/adaptador Loyverse. La medición combinada añade API y Persistence.
Los porcentajes de backend se conservan como los informa Coverlet en Cobertura y no son un promedio simple de módulos.
El frontend mide 13 de 18 archivos .js presentes. No incluye los componentes .jsx ni representa cobertura de todas las pantallas React.
Los registros LCOV duplicados del mismo archivo se unen por número de línea; auth.js se recarga con query strings en distintas pruebas. No se consolida la cobertura de ramas de esas instancias.

## Backend por módulo, ejecución combinada

| Módulo | Líneas | Ramas |
| --- | ---: | ---: |
| LoyOrder.Application | 80.20% | 80.13% |
| LoyOrder.Domain | 100.00% | No aplica |
| LoyOrder.Infrastructure.Persistence | 70.33% | 61.79% |
| LoyOrder.Api | 63.54% | 52.54% |
| LoyOrder.Infrastructure.Loyverse | 93.75% | 85.13% |

## Límites y siguientes pruebas

- La cobertura mide código ejecutado, no demuestra que todos los requisitos estén terminados ni que todos los resultados sean correctos.
- El transporte SMTP y los bucles de trabajadores tienen menor cobertura automatizada. La recepción de avisos y el recibo real se documentan por separado como pruebas manuales.
- Las pruebas HTTP no son pruebas unitarias; se presentan separadas aunque también ayudan a medir código del backend.
- La impresión ESC/POS sigue pendiente del módulo de Carlos. Un módulo todavía no implementado no puede aumentar el denominador de cobertura.
- El recorrido de venta con envío requiere configurar una variante real para el costo de envío y comprobarlo en la cuenta de pruebas.

## Repetir la medición

Desde la raíz del proyecto, con .NET 10, Node.js y Python 3 instalados:

```powershell
python tests/generar_cobertura.py
```

Para incluir PostgreSQL, definir localmente LOYORDER_PRUEBAS_ADMIN y ejecutar:

```powershell
python tests/generar_cobertura.py --incluir-postgres
```

La suite PostgreSQL crea y elimina exclusivamente la base de pruebas loyorder_pruebas_sync; no debe existir allí información que se necesite conservar. Esta medición se ejecutó sobre un servidor PostgreSQL temporal separado del entorno de Aldo.
Sin --incluir-postgres el reporte indica que esa suite no se ejecutó y no presenta su resultado como aprobado.
El script restaura Coverlet desde el manifiesto, compila una vez y ejecuta las DLL sin recompilarlas mientras están instrumentadas. Produce JSON, Cobertura XML, LCOV, registros y este resumen en una carpeta nueva de TestResults/cobertura.

Herramientas: Coverlet.Console 10.1.1 y cobertura nativa de Node.js. La unión de líneas de LCOV y las tasas de Cobertura se pueden revisar en los archivos de evidencia.

## Evidencia conservada

Los XML Cobertura de backend unitario y combinado, el LCOV del frontend y los conteos JSON están en [evidencia de la medición](evidencias/cobertura_2026_11_26.zip). El archivo JSON incluye las versiones de herramientas.
