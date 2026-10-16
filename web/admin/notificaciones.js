/*
 * Sección «Notificaciones»: enviar una notificación push a una cuenta o a un
 * hogar (todos sus residentes), a iPhone (APNs) y Android (FCM).
 *
 * Va en su propio archivo para no tocar admin.js: usa sus funciones (json,
 * esc, chip, kpi, fechaLima, $) y se registra en SECCIONES.
 */
(() => {
  const EMOJIS = {
    'Caras': '😀 😃 😄 😁 😊 🙂 😉 😍 🥳 😎 🤔 😮 😴 😅 😂 🥲 😢 😡 🤯 🙏'.split(' '),
    'Gestos': '👋 👍 👎 👏 🙌 💪 👀 ✋ 🤝 ✌️ 🫶 👉'.split(' '),
    'Seguridad': '🚨 🔔 🔕 🛡️ 🔒 🔓 🔑 📷 📹 🎥 🏠 🏡 🚪 🪟 👤 👥 🕵️ ⚠️ ❗ ✅ ❌ 🆘'.split(' '),
    'Objetos': '📱 💡 🔋 📶 🛠️ ⚙️ 📅 ⏰ ⏳ 📍 📣 📢 💬 📝 🎁 🎉'.split(' '),
    'Símbolos': '❤️ ⭐ ✨ 🔥 💯 🆕 🆗 ℹ️ ➡️ ⬆️ ⬇️ 🟢 🟡 🔴 ⚫ ⚪'.split(' '),
  };
  const PLANTILLAS = [
    { nombre: 'Elegir una plantilla…' },
    { nombre: 'Prueba', titulo: 'Notificación de prueba 🔔', cuerpo: 'Si ves esto, las notificaciones de VigiShield te llegan bien. ✅', nivel: 'normal' },
    { nombre: 'Servidor en pausa', titulo: 'Servidor en pausa ⏳', subtitulo: 'Mantenimiento', cuerpo: 'El análisis de video está apagado un momento. Tus cámaras y tu historial siguen guardados.', nivel: 'normal' },
    { nombre: 'Servidor activo', titulo: 'VigiShield vigila de nuevo 🛡️', cuerpo: 'El análisis de video ya está encendido. ¡Gracias por tu paciencia!', nivel: 'normal' },
    { nombre: 'Nueva versión', titulo: 'Nueva versión disponible 🆕', cuerpo: 'Actualiza VigiShield para tener las últimas mejoras.', nivel: 'normal' },
    { nombre: 'Sesión de prueba', titulo: 'Hoy probamos VigiShield 📷', cuerpo: 'Deja tu cámara encendida y apuntando a la entrada. ¡Gracias por participar! 🙌', nivel: 'urgente' },
  ];

  let usuarios = [];
  let dispositivos = new Map();   // usuarioId → { ios, android }
  let ultimoCampo = null;         // campo de texto donde insertar el emoji

  const telefonos = (ids) => ids.reduce((t, id) => {
    const d = dispositivos.get(id) || { ios: 0, android: 0 };
    return { ios: t.ios + d.ios, android: t.android + d.android };
  }, { ios: 0, android: 0 });
  const etiquetaTelefonos = ({ ios, android }) =>
    ios + android === 0 ? 'sin teléfonos registrados' : [ios && `${ios} iPhone`, android && `${android} Android`].filter(Boolean).join(' · ');

  function hogares() {
    const porHogar = new Map();
    for (const u of usuarios) {
      if (!porHogar.has(u.hogarId)) porHogar.set(u.hogarId, []);
      porHogar.get(u.hogarId).push(u);
    }
    return [...porHogar.entries()].map(([id, miembros]) => {
      const dueno = miembros.find((m) => m.rol !== 'Secondary') || miembros[0];
      return { id, dueno, miembros, tel: telefonos(miembros.map((m) => m.id)) };
    }).sort((a, b) => a.dueno.nombre.localeCompare(b.dueno.nombre));
  }

  function pintarDestinos() {
    const tipo = $('#ntTipo').value;
    const q = $('#ntBuscar').value.trim().toLowerCase();
    const opciones = tipo === 'usuario'
      ? usuarios.filter((u) => !q || `${u.nombre} ${u.correo}`.toLowerCase().includes(q))
        .map((u) => ({ id: u.id, texto: `${u.nombre} · ${u.correo} — ${etiquetaTelefonos(telefonos([u.id]))}`, tel: telefonos([u.id]) }))
      : hogares().filter((h) => !q || h.miembros.some((m) => `${m.nombre} ${m.correo}`.toLowerCase().includes(q)))
        .map((h) => ({ id: h.id, texto: `Hogar de ${h.dueno.nombre} (${h.miembros.length} ${h.miembros.length === 1 ? 'residente' : 'residentes'}) — ${etiquetaTelefonos(h.tel)}`, tel: h.tel }));
    const previo = $('#ntDestino').value;
    $('#ntDestino').innerHTML = opciones.length
      ? opciones.map((o) => `<option value="${esc(o.id)}" ${o.tel.ios + o.tel.android === 0 ? 'class="sin-tel"' : ''}>${esc(o.texto)}</option>`).join('')
      : '<option value="">Nada coincide con la búsqueda</option>';
    if (opciones.some((o) => o.id === previo)) $('#ntDestino').value = previo;
    pintarVista();
  }

  function datos() {
    const v = (id) => $(id).value.trim();
    return {
      destino: $('#ntTipo').value,
      id: $('#ntDestino').value,
      titulo: v('#ntTitulo'),
      subtitulo: v('#ntSubtitulo') || null,
      cuerpo: v('#ntCuerpo'),
      imagenUrl: v('#ntImagen') || null,
      nivel: $('#ntNivel').value,
      sonido: $('#ntSonido').checked,
      insignia: v('#ntInsignia') === '' ? null : Number(v('#ntInsignia')),
      abrir: $('#ntAbrir').value,
      eventoId: v('#ntEvento') || null,
    };
  }

  function pintarVista() {
    const d = datos();
    const titulo = d.titulo || 'Título de la notificación';
    const cuerpo = d.cuerpo || 'El mensaje aparece aquí.';
    for (const p of ['Ios', 'Android']) {
      $(`#vista${p} .vt-titulo`).textContent = titulo;
      $(`#vista${p} .vt-cuerpo`).textContent = p === 'Android' && d.subtitulo ? `${d.subtitulo}\n${cuerpo}` : cuerpo;
      const img = $(`#vista${p} .vt-imagen`);
      img.classList.toggle('oculto', !d.imagenUrl);
      if (d.imagenUrl) img.src = d.imagenUrl;
    }
    $('#vistaIos .vt-sub').textContent = d.subtitulo || '';
    $('#vistaIos .vt-sub').classList.toggle('oculto', !d.subtitulo);
    $('#vistaIos .vt-insignia').textContent = d.insignia ?? '';
    $('#vistaIos .vt-insignia').classList.toggle('oculto', d.insignia == null);
    const nivel = { urgente: 'Urgente · atraviesa «No molestar» y los modos de concentración', normal: 'Normal · suena y aparece arriba',
      silencioso: 'Silenciosa · sin sonido ni aviso arriba; queda en el centro de notificaciones' }[d.nivel];
    $('#ntNivelAyuda').textContent = nivel + (d.sonido ? '' : ' · sin sonido');
    $('#ntEventoFila').classList.toggle('oculto', d.abrir !== 'evento');
    $('#ntCuenta').textContent = `${d.titulo.length}/120 · ${d.cuerpo.length}/1000`;
  }

  function insertar(emoji) {
    const campo = ultimoCampo || $('#ntCuerpo');
    const ini = campo.selectionStart ?? campo.value.length;
    const fin = campo.selectionEnd ?? campo.value.length;
    campo.value = campo.value.slice(0, ini) + emoji + campo.value.slice(fin);
    campo.focus();
    campo.selectionStart = campo.selectionEnd = ini + emoji.length;
    pintarVista();
  }

  async function enviar() {
    const d = datos();
    const res = $('#ntResultado');
    if (!d.id) { res.innerHTML = chip('Elige a quién enviarla', 'warn'); return; }
    if (!d.titulo || !d.cuerpo) { res.innerHTML = chip('Falta el título o el mensaje', 'warn'); return; }
    if (d.abrir === 'evento' && !d.eventoId) { res.innerHTML = chip('Falta el id del evento', 'warn'); return; }
    const a = $('#ntDestino').selectedOptions[0]?.textContent || '';
    if (!confirm(`¿Enviar «${d.titulo}» a ${a.split(' — ')[0]}?`)) return;
    const boton = $('#ntEnviar');
    boton.disabled = true;
    res.textContent = 'Enviando…';
    try {
      const r = await json(`${API}/admin/push/enviar`, { method: 'POST', body: JSON.stringify(d) });
      const avisos = [];
      if (r.ios && !r.iosConfigurado) avisos.push('el servidor no tiene la llave de iPhone');
      if (r.android && !r.androidConfigurado) avisos.push('el servidor no tiene Firebase para Android');
      res.innerHTML = r.dispositivos === 0
        ? `${chip('Sin teléfonos', 'warn')} Ese destino no tiene teléfonos registrados (tienen que abrir la app con sesión iniciada y aceptar las notificaciones).`
        : `${chip(r.enviados === r.dispositivos ? 'Enviada' : `Enviada a ${r.enviados} de ${r.dispositivos}`, r.enviados ? 'ok' : 'bad')}
           ${r.ios} iPhone · ${r.android} Android${r.invalidos ? ` · ${r.invalidos} teléfonos ya no existían y se dieron de baja` : ''}${r.fallidos ? ` · ${r.fallidos} fallaron` : ''}${avisos.length ? ` · ${esc(avisos.join(', '))}` : ''}`;
      await cargar();
    } catch (e) {
      res.innerHTML = `${chip('Error', 'bad')} ${esc(e.message)}`;
    } finally {
      boton.disabled = false;
    }
  }

  async function cargar() {
    const [lista, porUsuario] = await Promise.all([json(`${API}/admin/usuarios`), json(`${API}/admin/push/dispositivos`)]);
    usuarios = lista;
    dispositivos = new Map(porUsuario.map((d) => [d.usuarioId, { ios: d.ios, android: d.android }]));
    const total = porUsuario.reduce((t, d) => ({ ios: t.ios + d.ios, android: t.android + d.android }), { ios: 0, android: 0 });
    $('#kpisNotif').innerHTML = [
      kpi(porUsuario.length, 'cuentas con teléfono registrado'),
      kpi(total.ios, 'iPhone'),
      kpi(total.android, 'Android'),
    ].join('');
    pintarDestinos();
  }

  async function verNotificaciones() {
    $('#ntEmojis').innerHTML = Object.entries(EMOJIS).map(([grupo, lista]) =>
      `<div class="nt-grupo"><small>${esc(grupo)}</small><div>${lista.map((e) =>
        `<button type="button" class="nt-emoji" data-emoji="${esc(e)}" title="${esc(e)}">${esc(e)}</button>`).join('')}</div></div>`).join('');
    $('#ntPlantilla').innerHTML = PLANTILLAS.map((p, i) => `<option value="${i}">${esc(p.nombre)}</option>`).join('');
    $('#ntPlantilla').onchange = () => {
      const p = PLANTILLAS[Number($('#ntPlantilla').value)];
      if (!p.titulo) return;
      $('#ntTitulo').value = p.titulo;
      $('#ntSubtitulo').value = p.subtitulo || '';
      $('#ntCuerpo').value = p.cuerpo;
      $('#ntNivel').value = p.nivel || 'normal';
      pintarVista();
    };
    $('#ntEmojis').onmousedown = (e) => e.preventDefault();   // que el campo no pierda el cursor
    $('#ntEmojis').onclick = (e) => { const b = e.target.closest('[data-emoji]'); if (b) insertar(b.dataset.emoji); };
    for (const id of ['#ntTitulo', '#ntSubtitulo', '#ntCuerpo']) $(id).onfocus = (e) => { ultimoCampo = e.target; };
    for (const id of ['#ntTitulo', '#ntSubtitulo', '#ntCuerpo', '#ntImagen', '#ntInsignia', '#ntEvento']) $(id).oninput = pintarVista;
    for (const id of ['#ntNivel', '#ntSonido', '#ntAbrir', '#ntDestino']) $(id).onchange = pintarVista;
    $('#ntTipo').onchange = pintarDestinos;
    $('#ntBuscar').oninput = pintarDestinos;
    $('#ntEnviar').onclick = enviar;
    $('#vistaIos .vt-imagen').onerror = $('#vistaAndroid .vt-imagen').onerror = (e) => e.target.classList.add('oculto');
    await cargar();
  }

  SECCIONES.notificaciones = verNotificaciones;
  if (location.hash === '#notificaciones') irA();
})();
