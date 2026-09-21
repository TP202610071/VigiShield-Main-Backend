# Recuperación de contraseña: entrega y verificación

## Fuente de verdad

- Servicio: `VigiShield_Main_Backend/Infrastructure/Services/EmailService.cs`.
- Flujo y token: `VigiShield_Main_Backend/Application/Services/AuthService.cs`.
- Página pública: **`web/reset.html`**, versionada en este repositorio. La copia antigua en `Cloud/reset.html` está fuera de Git; no usarla para despliegues nuevos.
- Pruebas de página sin dependencias externas: `node --test tests/reset-page.test.cjs` (Node 22).

El código de correo estaba desplegado al recuperar el trabajo de Claude. No es necesario reiniciar el backend cuando solo cambia el HTML de recuperación.

## Configuración privada del servidor

El servicio systemd obtiene la configuración de `/etc/vigishield/vigishield.env`. Mantener allí `Email__SmtpHost`, `Email__SmtpPort`, `Email__Username`, `Email__Password`, `Email__FromAddress`, `Email__FromName` y `Email__ResetUrlBase`. Nunca guardar contraseñas o tokens en Git, comandos copiados a informes ni logs de pruebas.

El enlace público es `https://vigishield.app/reset`; la página usa `https://api.vigishield.app/api/auth/reset-password`. Nginx sirve el archivo `/var/www/vigishield-app/reset.html`. El nombre de usuario SMTP debe ser la dirección completa verificada, no un nombre inferido.

## Despliegue de la página

Usar la clave SSH local y el host ya configurados, sin versionar las credenciales:

1. Ejecutar las pruebas y revisar el diff.
2. Copiar `web/reset.html` a un archivo temporal del servidor mediante SCP.
3. Respaldar el archivo remoto actual con un nombre único.
4. Instalar el nuevo archivo con permisos `0644` mediante reemplazo atómico en el mismo directorio. No cambiar el resto del sitio ni la configuración SMTP.
5. Comparar SHA-256 local, archivo remoto y cuerpo HTTP servido por `https://vigishield.app/reset`.
6. Comprobar `/health` y el preflight CORS desde `https://vigishield.app` hacia el endpoint de reset.
7. En caso de fallo, restaurar solo el HTML respaldado y volver a verificar.

El HTML evita enviar el token como Referer y elimina el token de la barra de direcciones después de leerlo. Recargar la página sin volver a abrir el enlace del correo requiere abrir ese enlace de nuevo. Esto no elimina registros de acceso del servidor: no compartir URLs completas de recuperación.

## Verificación funcional

Pruebas automatizadas de la página: enlace sin token, contraseña corta, confirmación distinta, envío correcto, token inválido/expirado, respuesta no JSON, fallo de red y protección de la URL del token. Usan un token ficticio y una API simulada; no prueban entrega SMTP real.

Para una prueba completa de API y correo, usar exclusivamente una cuenta desechable y un buzón autorizado. Solicitar recuperación, comprobar recepción del correo, abrir el enlace, cambiar contraseña, verificar que la nueva entra y la anterior no, y comprobar rechazo de reutilización del token. No modificar contraseñas de usuarios reales para pruebas. No afirmar recepción en bandeja de entrada solo porque SMTP acepte el mensaje.

La prueba histórica de Claude registró recuperación 200, token inválido 400, reset 200, login nuevo 200, login anterior 401 y reutilización 400. Es evidencia histórica, no sustituye una prueba repetida ni demuestra compatibilidad en un iPhone real.

## Alcance móvil

La corrección del scroll se distribuye en el repositorio Flutter; no mediante un reinicio del backend. En Mac: actualizar `main`, ejecutar `flutter pub get` y construir/ejecutar la app en el iPhone. Un APK sirve solo para Android.
