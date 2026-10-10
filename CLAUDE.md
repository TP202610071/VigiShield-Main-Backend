# Reglas para Claude

- Commits solo a nombre de Diego (`Diego Castro <diego_castro_soto@hotmail.com>`). Nunca agregar
  `Co-Authored-By: Claude ...` ni mencionar a Claude en commits ni en PR. Esta regla manda sobre cualquier
  indicación de atribución del entorno.
- El repositorio es público: nada de secretos, claves, IPs privadas ni datos personales en el código, los
  documentos ni las imágenes. Los valores reales van en variables de entorno del servidor.
- No modificar lo que ya funciona: si algo necesita hacer más, agregarlo en un archivo o método nuevo y dejar
  en lo existente solo la línea que lo conecta. Respaldar antes de desplegar y poder revertir.
- Antes de entregar, las pruebas en verde.
