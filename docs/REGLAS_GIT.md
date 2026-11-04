# Reglas Git del equipo

1. Se trabaja directamente en la rama `main`.
2. Antes de empezar a trabajar, traer los cambios del equipo: `git pull origin main`.
3. Hacer commits pequeños y que compilen; no subir código que rompa `dotnet build`.
4. Antes de subir, volver a traer cambios con `git pull --rebase origin main` y resolver los conflictos si aparecen.
5. Correr las pruebas antes de subir (`dotnet run --project tests/unit/...`).
6. Avisar al equipo qué se subió y cómo se comprobó.
7. No subir secretos ni credenciales: tokens de Loyverse, contraseñas ni archivos `.env` reales. El token de Loyverse se guarda con `dotnet user-secrets` o en la variable `LOYVERSE_ACCESS_TOKEN`.
8. Usar mensajes con formato `feat:`, `fix:`, `docs:`, `test:`, `refactor:` o `chore:`.
