# Decisiones técnicas de LoyOrder

Backend: ASP.NET Core y C#, confirmado por Aldo el 5 de noviembre de 2026.
Base de datos: PostgreSQL, confirmado en la misma sesión.
Versión del backend: .NET 10 para conservar compatibilidad con el cliente Loyverse existente.
Frontend: React con Vite. Direcciones: selector con Mapbox incorporado por el equipo en 11a8645. Correo: transporte SMTP con MailKit y Gmail propuesto para configurar el remitente. Pruebas actuales: ejecutables .NET de API y Loyverse, Node para frontend y comprobaciones con PostgreSQL temporal.

La actividad 7.1.3 usa controladores HTTP, aplicación, dominio y persistencia Npgsql con SQL parametrizado. No altera el esquema 7.1.2 ni genera migraciones automáticas.
El módulo de Loyverse se conserva sin cambios; su conexión a casos de uso corresponde a actividades posteriores.
