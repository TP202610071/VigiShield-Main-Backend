/* Panel de administración de VigiShield.
 *
 * Todo pasa por el mismo dominio (vigishield.app/admin):
 *   /admin/api/...  → backend (Oracle)      /admin/ai/... → VM de IA (Azure)
 * El token vive en sessionStorage (se borra al cerrar la pestaña) y el
 * servidor exige rol Admin en cada petición: ocultar cosas aquí no protege nada.
 */
'use strict';

const API = '/admin/api';
const IA = '/admin/ai';
const CLAVE_TOKEN = 'vs_admin_token';

const TIPOS = {
  UnknownFace: 'Persona desconocida', Tailgating: 'Merodeo', SuspiciousIntent: 'Riesgo de intrusión',
  WeaponDetected: 'Arma detectada', FaceRecognized: 'Acceso reconocido', ForcedAccessAttempt: 'Intento de acceso forzado',
  PhysicalAggression: 'Agresión física', Burglary: 'Allanamiento', Robbery: 'Robo a mano armada', Stealing: 'Hurto',
};
const ALERTABLES = ['UnknownFace', 'Tailgating', 'SuspiciousIntent', 'WeaponDetected', 'ForcedAccessAttempt',
  'PhysicalAggression', 'Burglary', 'Robbery', 'Stealing'];
const INTENCION = { calm: 'Normal', watch: 'Vigilando', suspect: 'Sospechoso', high_risk: 'Riesgo alto' };
const RIESGO = { None: ['Ninguno', ''], Low: ['Bajo', 'info'], Medium: ['Medio', 'warn'], High: ['Alto', 'bad'], Critical: ['Crítico', 'bad'] };

const $ = (s) => document.querySelector(s);
const $$ = (s) => [...document.querySelectorAll(s)];
const esc = (v) => String(v ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

let token = sessionStorage.getItem(CLAVE_TOKEN);
let camaras = [], usuarios = [], streams = { paths: [], ia: {} };
let temporizadores = [];
let paginaEventos = 1;
const urlsFoto = new Map();   // camara → objectURL actual (para liberarlos)

// ── HTTP ─────────────────────────────────────────────────────────────────────

async function pedir(ruta, opciones = {}) {
  const resp = await fetch(ruta, {
    ...opciones,
    headers: { ...(opciones.headers || {}), ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(opciones.body && !(opciones.body instanceof FormData) ? { 'Content-Type': 'application/json' } : {}) },
  });
  // Solo el backend decide la sesión: si la VM de IA rechaza o está caída,
  // se avisa en su sección pero no se cierra el panel.
  if ((resp.status === 401 || resp.status === 403) && ruta.startsWith(API)) {
    salir('Tu sesión venció o no tienes permiso de administrador.');
    throw new Error('sin permiso');
  }
  if (!resp.ok) {
    let msg = `Error ${resp.status}`;
    try { const j = await resp.json(); msg = j.error || j.message || j.title || msg; } catch { /* sin cuerpo */ }
    throw new Error(msg);
  }
  return resp;
}
const json = async (ruta, op) => (await pedir(ruta, op)).json();

// ── Sesión ───────────────────────────────────────────────────────────────────

function rolDelToken(t) {
  try {
    const p = JSON.parse(atob(t.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')));
    return { rol: p.role || p['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'],
      nombre: p.unique_name || p.name || p.email, exp: p.exp };
  } catch { return {}; }
}

$('#formLogin').addEventListener('submit', async (e) => {
  e.preventDefault();
  const err = $('#errorLogin');
  err.textContent = '';
  try {
    const r = await fetch(`${API}/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: $('#correo').value.trim(), password: $('#clave').value }) });
    if (!r.ok) { err.textContent = r.status === 429 ? 'Demasiados intentos. Espera un momento.' : 'Correo o contraseña incorrectos.'; return; }
    const datos = await r.json();
    if ((datos.user?.role ?? rolDelToken(datos.token).rol) !== 'Admin') {
      err.textContent = 'Esta cuenta no es administradora.';
      return;
    }
    token = datos.token;
    sessionStorage.setItem(CLAVE_TOKEN, token);
    $('#clave').value = '';
    entrar();
  } catch {
    err.textContent = 'No se pudo conectar con el servidor.';
  }
});

function salir(mensaje) {
  token = null;
  sessionStorage.removeItem(CLAVE_TOKEN);
  pararTemporizadores();
  $('#app').classList.add('oculto');
  $('#login').classList.remove('oculto');
  $('#errorLogin').textContent = mensaje || '';
}
$('#salir').addEventListener('click', (e) => { e.preventDefault(); salir(); });

function entrar() {
  const info = rolDelToken(token);
  if (info.rol !== 'Admin' || (info.exp && info.exp * 1000 < Date.now())) { salir(); return; }
  $('#quien').textContent = info.nombre || '';
  $('#login').classList.add('oculto');
  $('#app').classList.remove('oculto');
  cargarListasBase().then(irA);
}

// ── Navegación ───────────────────────────────────────────────────────────────

function pararTemporizadores() {
  temporizadores.forEach(clearInterval);
  temporizadores = [];
  for (const url of urlsFoto.values()) URL.revokeObjectURL(url);
  urlsFoto.clear();
}
const cada = (ms, fn) => { fn(); temporizadores.push(setInterval(fn, ms)); };

const SECCIONES = { resumen: verResumen, vivo: verVivo, camaras: verCamaras, usuarios: verUsuarios,
  eventos: verEventos, alertas: verAlertas, servidores: verServidores, admins: verAdmins };

function irA() {
  if (!token) return;
  const s = (location.hash || '#resumen').slice(1);
  const seccion = SECCIONES[s] ? s : 'resumen';
  pararTemporizadores();
  cerrarModal();
  $$('main > section').forEach((el) => el.classList.toggle('oculto', el.dataset.seccion !== seccion));
  $$('nav.lateral a[data-seccion]').forEach((a) => a.classList.toggle('activo', a.dataset.seccion === seccion));
  SECCIONES[seccion]().catch((e) => console.error(e));
}
window.addEventListener('hashchange', irA);

async function cargarListasBase() {
  [camaras, usuarios] = await Promise.all([json(`${API}/admin/camaras`), json(`${API}/admin/usuarios`)]);
  const opciones = usuarios.filter((u) => u.rol !== 'Secondary')
    .map((u) => `<option value="${esc(u.hogarId)}">${esc(u.nombre)} · ${esc(u.correo)}</option>`).join('');
  $('#evUsuario').innerHTML = '<option value="">Todos</option>' + opciones;
  $('#alUsuario').innerHTML = opciones;
  $('#evTipo').innerHTML = '<option value="">Todos</option>' +
    Object.entries(TIPOS).map(([k, v]) => `<option value="${k}">${esc(v)}</option>`).join('');
  $('#alTipo').innerHTML = ALERTABLES.map((k) => `<option value="${k}">${esc(TIPOS[k])}</option>`).join('');
}

async function cargarStreams() {
  try { streams = await json(`${IA}/admin/streams`); } catch { streams = { paths: [], ia: {} }; }
}

/** La cámara está transmitiendo (hay publicador en MediaMTX) y/o la IA la procesa ahora. */
function estadoCamara(c) {
  const path = streams.paths.find((p) => p.clave === c.streamKey);
  const ia = streams.ia[c.id];
  const procesando = ia && ia.hace < 15;
  const transmitiendo = (path && path.listo) || procesando;
  return { transmitiendo, procesando, ia, path };
}

// ── Formatos ─────────────────────────────────────────────────────────────────

const fechaLima = (iso) => iso ? new Date(iso).toLocaleString('es-PE', { timeZone: 'America/Lima', dateStyle: 'short', timeStyle: 'short' }) : '—';
function hace(iso) {
  if (!iso) return '—';
  const s = Math.round((Date.now() - new Date(iso).getTime()) / 1000);
  if (s < 60) return 'hace un momento';
  if (s < 3600) return `hace ${Math.round(s / 60)} min`;
  if (s < 86400) return `hace ${Math.round(s / 3600)} h`;
  return `hace ${Math.round(s / 86400)} d`;
}
const chip = (texto, tipo = '') => `<span class="chip ${tipo}">${esc(texto)}</span>`;
const kpi = (valor, texto) => `<div class="kpi"><b>${esc(valor)}</b><span>${esc(texto)}</span></div>`;

// ── Resumen ──────────────────────────────────────────────────────────────────

let graficoTipos;
async function verResumen() {
  cada(15000, async () => {
    const [r] = await Promise.all([json(`${API}/admin/resumen`), cargarStreams()]);
    const vivas = camaras.filter((c) => estadoCamara(c).transmitiendo).length;
    $('#kpis').innerHTML = [
      kpi(r.usuarios, 'usuarios registrados'), kpi(r.usuariosActivos7Dias, 'activos (7 días)'),
      kpi(r.usuariosEnLinea, 'en línea ahora'), kpi(vivas, 'cámaras transmitiendo'),
      kpi(`${r.camarasActivas}/${r.camaras}`, 'cámaras con IA activa'), kpi(r.eventos24h, 'eventos (24 h)'),
      kpi(r.eventos7Dias, 'eventos (7 días)'), kpi(r.sesionesVideoEjemplo, 'videos de ejemplo vistos'),
    ].join('');
    const tipos = Object.entries(r.eventosPorTipo7Dias || {}).sort((a, b) => b[1] - a[1]);
    const datos = { labels: tipos.map((t) => t[0]), datasets: [{ data: tipos.map((t) => t[1]), backgroundColor: '#00C2FF' }] };
    if (!graficoTipos) {
      graficoTipos = new Chart($('#graficoTipos'), { type: 'bar', data: datos,
        options: { plugins: { legend: { display: false } }, scales: estiloEjes(), indexAxis: 'y' } });
    } else { graficoTipos.data = datos; graficoTipos.update(); }
    const [az, or] = await Promise.allSettled([json(`${IA}/admin/metricas`), json(`${API}/admin/metricas`)]);
    $('#resumenServidores').innerHTML = [
      az.status === 'fulfilled' ? kpi(`${az.value.cpu}%`, 'CPU · IA (Azure)') : kpi('—', 'IA sin respuesta'),
      az.status === 'fulfilled' ? kpi(`${az.value.ram}%`, 'RAM · IA (Azure)') : '',
      or.status === 'fulfilled' ? kpi(`${or.value.cpu}%`, 'CPU · Backend (Oracle)') : kpi('—', 'Backend sin respuesta'),
      or.status === 'fulfilled' ? kpi(`${or.value.ram}%`, 'RAM · Backend (Oracle)') : '',
    ].join('');
  });
}

// ── En vivo ──────────────────────────────────────────────────────────────────

async function verVivo() {
  camaras = await json(`${API}/admin/camaras`);
  const pintar = async () => {
    await cargarStreams();
    const vivas = camaras.filter((c) => estadoCamara(c).procesando);
    const actuales = $$('#mosaico .vivo').map((el) => el.dataset.id).join(',');
    if (actuales === vivas.map((c) => c.id).join(',') && vivas.length) {
      vivas.forEach((c) => {
        const ia = streams.ia[c.id];
        const el = $(`#mosaico .vivo[data-id="${c.id}"] .info`);
        if (el && ia) el.textContent = `${ia.personas} persona(s) · ${INTENCION[ia.intencion] || 'Normal'}`;
      });
      return;
    }
    if (!vivas.length) {
      $('#mosaico').innerHTML = '<div class="vacio">Ninguna cámara se está analizando ahora.</div>';
      return;
    }
    $('#mosaico').innerHTML = vivas.map((c) => `
      <div class="vivo" data-id="${esc(c.id)}">
        <div class="cuadro"><span>Cargando…</span><span class="etiqueta">${chip('EN VIVO', 'bad')}</span></div>
        <div class="datos"><div><b>${esc(c.nombre)}</b><br><small>${esc(c.dueno || '')}</small></div>
          <small class="info"></small></div>
      </div>`).join('');
    $$('#mosaico .vivo').forEach((el) => el.addEventListener('click', () => abrirVivo(camaras.find((c) => c.id === el.dataset.id))));
  };
  cada(5000, pintar);
  cada(1000, () => $$('#mosaico .vivo').forEach((el) => cuadroIA(el.dataset.id, el.querySelector('.cuadro'))));
}

/** Pide el último cuadro anotado por la IA y lo pone en el contenedor. */
async function cuadroIA(camaraId, contenedor) {
  if (!contenedor || contenedor.dataset.pidiendo) return;
  contenedor.dataset.pidiendo = '1';
  try {
    const r = await fetch(`${IA}/frame/${encodeURIComponent(camaraId)}`, { headers: { Authorization: `Bearer ${token}` }, cache: 'no-store' });
    if (!r.ok) return;
    const url = URL.createObjectURL(await r.blob());
    let img = contenedor.querySelector('img');
    if (!img) { contenedor.innerHTML = '<img class="grande" alt="">' + (contenedor.querySelector('.etiqueta')?.outerHTML || ''); img = contenedor.querySelector('img'); }
    img.src = url;
    const clave = contenedor.id || camaraId;
    if (urlsFoto.has(clave)) URL.revokeObjectURL(urlsFoto.get(clave));
    urlsFoto.set(clave, url);
  } catch { /* el siguiente intento lo reintenta */ } finally { delete contenedor.dataset.pidiendo; }
}

let temporizadorModal = [];
function abrirVivo(c) {
  if (!c) return;
  abrirModal(`${c.nombre} · ${c.dueno || ''}`, `
    <div class="cuadro" id="cuadroGrande" style="aspect-ratio:16/9;background:#000;display:grid;place-items:center;border-radius:10px;overflow:hidden">Cargando…</div>
    <div class="estado" id="estadoGrande"></div>`);
  const t1 = setInterval(() => cuadroIA(c.id, $('#cuadroGrande')), 250);
  const t2 = setInterval(async () => {
    try {
      const s = await json(`${IA}/status/${encodeURIComponent(c.id)}`);
      $('#estadoGrande').innerHTML = [chip(`${s.persons ?? 0} persona(s)`, 'info'),
        chip(`Intención: ${INTENCION[s.intent && s.intent.state] || '—'} ${s.intent ? '(' + s.intent.score + ')' : ''}`, s.suspicious ? 'bad' : 'ok'),
        ...(s.alerts || []).map((a) => chip(a, 'warn'))].join(' ');
    } catch { /* sin estado todavía */ }
  }, 1000);
  temporizadorModal = [t1, t2];
}

// ── Cámaras ──────────────────────────────────────────────────────────────────

async function verCamaras() {
  const pintar = async () => {
    [camaras] = await Promise.all([json(`${API}/admin/camaras`), cargarStreams()]);
    const q = $('#buscarCamara').value.trim().toLowerCase();
    const f = $('#filtroCamaras').value;
    const lista = camaras.filter((c) => {
      const est = estadoCamara(c);
      if (f === 'vivo' && !est.transmitiendo) return false;
      if (f === 'activas' && !c.activa) return false;
      if (f === 'inactivas' && c.activa) return false;
      return !q || `${c.nombre} ${c.dueno} ${c.correoDueno}`.toLowerCase().includes(q);
    });
    const modos = { MobileWebRtc: 'Celular', DirectRtsp: 'IP (RTSP)', RtmpRelay: 'IP (relay)' };
    $('#tablaCamaras').innerHTML = lista.length ? lista.map((c) => {
      const est = estadoCamara(c);
      return `<tr>
        <td><span class="punto ${est.transmitiendo ? 'vivo' : ''}"></span>${est.procesando ? 'Analizando' : est.transmitiendo ? 'Transmitiendo' : 'Sin señal'}</td>
        <td>${esc(c.nombre)} ${c.esEjemplo ? chip('ejemplo', 'info') : ''}</td>
        <td>${esc(c.dueno || '—')}<br><small style="color:var(--muted)">${esc(c.correoDueno || '')}</small></td>
        <td>${esc(modos[c.modo] || c.modo)}</td>
        <td>${c.zonas}</td>
        <td>${c.esEjemplo ? '—' : `<label class="sw" title="Analizar con IA"><input type="checkbox" data-camara="${esc(c.id)}" ${c.activa ? 'checked' : ''}><span></span></label>`}</td>
        <td>${fechaLima(c.creadaEn)}</td>
        <td>${est.procesando ? `<button class="btn sec" data-ver="${esc(c.id)}">Ver en vivo</button>` : ''}</td>
      </tr>`;
    }).join('') : '<tr><td colspan="8" class="vacio">Sin cámaras para este filtro.</td></tr>';
  };
  $('#buscarCamara').oninput = pintar;
  $('#filtroCamaras').onchange = pintar;
  $('#tablaCamaras').onchange = async (e) => {
    const id = e.target.dataset.camara;
    if (!id) return;
    e.target.disabled = true;
    try {
      await json(`${API}/admin/camaras/${id}/activa`, { method: 'PUT', body: JSON.stringify({ activa: e.target.checked }) });
    } catch (err) {
      e.target.checked = !e.target.checked;
      alert(`No se pudo cambiar: ${err.message}`);
    } finally { e.target.disabled = false; }
  };
  $('#tablaCamaras').onclick = (e) => {
    const id = e.target.dataset.ver;
    if (id) abrirVivo(camaras.find((c) => c.id === id));
  };
  cada(10000, pintar);
}

// ── Usuarios ─────────────────────────────────────────────────────────────────

async function verUsuarios() {
  const pintar = () => {
    const q = $('#buscarUsuario').value.trim().toLowerCase();
    const f = $('#filtroUsuarios').value;
    const lista = usuarios.filter((u) => (f === 'todos' || (f === 'linea' ? u.enLinea : u.estado === f))
      && (!q || `${u.nombre} ${u.correo}`.toLowerCase().includes(q)));
    $('#kpisUsuarios').innerHTML = [kpi(usuarios.length, 'registrados'),
      kpi(usuarios.filter((u) => u.estado === 'activo').length, 'activos (7 días)'),
      kpi(usuarios.filter((u) => u.estado === 'inactivo').length, 'inactivos'),
      kpi(usuarios.filter((u) => u.enLinea).length, 'en línea')].join('');
    const roles = { Primary: 'Principal', Secondary: 'Secundario', Admin: 'Admin' };
    $('#tablaUsuarios').innerHTML = lista.length ? lista.map((u) => `<tr>
      <td>${u.enLinea ? chip('En línea', 'ok') : u.estado === 'activo' ? chip('Activo', 'info') : chip('Inactivo')}</td>
      <td>${esc(u.nombre)}</td><td>${esc(u.correo)}</td><td>${esc(roles[u.rol] || u.rol)}</td>
      <td title="${esc(fechaLima(u.ultimaActividad))}">${hace(u.ultimaActividad)}</td>
      <td>${fechaLima(u.creadoEn)}</td>
      <td>${u.camarasActivas}/${u.camaras}</td><td>${u.eventos}</td><td>${hace(u.ultimoEvento)}</td>
      <td>${u.sesionesVideoEjemplo}</td><td>${u.terminosAceptados ? '✔' : '—'}</td></tr>`).join('')
      : '<tr><td colspan="11" class="vacio">Sin usuarios para este filtro.</td></tr>';
  };
  $('#buscarUsuario').oninput = pintar;
  $('#filtroUsuarios').onchange = pintar;
  cada(30000, async () => { usuarios = await json(`${API}/admin/usuarios`); pintar(); });
}

// ── Eventos ──────────────────────────────────────────────────────────────────

function filtroEventos(pagina) {
  const p = new URLSearchParams();
  if ($('#evUsuario').value) p.set('hogar', $('#evUsuario').value);
  if ($('#evTipo').value) p.set('tipo', $('#evTipo').value);
  if ($('#evOrigen').value) p.set('ejemplos', $('#evOrigen').value);
  // Las fechas se interpretan en hora de Lima (UTC−5).
  if ($('#evDesde').value) p.set('desde', `${$('#evDesde').value}T00:00:00-05:00`);
  if ($('#evHasta').value) p.set('hasta', `${$('#evHasta').value}T23:59:59-05:00`);
  if (pagina) { p.set('pagina', pagina); p.set('tamano', 50); }
  return p;
}

async function verEventos() {
  const cargar = async (pagina = 1) => {
    paginaEventos = pagina;
    const r = await json(`${API}/admin/eventos?${filtroEventos(pagina)}`);
    $('#tablaEventos').innerHTML = r.items.length ? r.items.map((e) => {
      const [riesgo, tono] = RIESGO[e.riesgo] || [e.riesgo, ''];
      return `<tr>
        <td>${e.foto ? `<img class="mini" src="${esc(e.foto)}" data-foto="${esc(e.foto)}" alt="" loading="lazy">` : '—'}</td>
        <td>${fechaLima(e.fecha)}</td>
        <td>${esc(e.dueno || '—')}<br><small style="color:var(--muted)">${esc(e.correoDueno || '')}</small></td>
        <td>${esc(e.camara || '—')}</td>
        <td>${esc(e.tipoEtiqueta)}</td><td>${chip(riesgo, tono)}</td>
        <td>${e.confianza != null ? Math.round(e.confianza * 100) + '%' : '—'}</td>
        <td>${e.esEjemplo ? chip('Video de ejemplo', 'info') : chip('Cámara real', 'ok')}</td>
        <td>${e.foto ? `<a href="${esc(e.foto)}" target="_blank" rel="noopener" download>Foto</a>` : ''}
            ${e.clip ? ` · <a href="#" data-clip="${esc(e.clip)}">Ver clip</a> · <a href="${esc(e.clip)}" target="_blank" rel="noopener" download>Clip</a>` : ''}</td>
      </tr>`;
    }).join('') : '<tr><td colspan="9" class="vacio">Sin eventos para este filtro.</td></tr>';
    const paginas = Math.max(1, Math.ceil(r.total / r.tamano));
    $('#paginasEventos').innerHTML = `${r.total} evento(s) · página ${r.pagina} de ${paginas}
      <button class="btn sec" data-pag="${r.pagina - 1}" ${r.pagina <= 1 ? 'disabled' : ''}>‹ Anterior</button>
      <button class="btn sec" data-pag="${r.pagina + 1}" ${r.pagina >= paginas ? 'disabled' : ''}>Siguiente ›</button>`;
  };
  $('#evBuscar').onclick = () => cargar(1);
  $('#paginasEventos').onclick = (e) => { const p = +e.target.dataset.pag; if (p) cargar(p); };
  $('#tablaEventos').onclick = (e) => {
    if (e.target.dataset.foto) abrirModal('Foto del evento', `<img class="grande" src="${esc(e.target.dataset.foto)}" alt="">`);
    if (e.target.dataset.clip) {
      e.preventDefault();
      abrirModal('Clip del evento', `<video src="${esc(e.target.dataset.clip)}" controls autoplay playsinline></video>`);
    }
  };
  $('#evExportar').onclick = exportar;
  await cargar(paginaEventos);
}

async function exportar() {
  const boton = $('#evExportar');
  boton.disabled = true;
  boton.textContent = 'Preparando ZIP…';
  try {
    const r = await pedir(`${API}/admin/eventos/exportar?${filtroEventos()}`);
    const blob = await r.blob();
    const nombre = (r.headers.get('Content-Disposition') || '').match(/filename="?([^"]+)"?/)?.[1] || 'vigishield_eventos.zip';
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = nombre;
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 60000);
  } catch (e) {
    alert(`No se pudo exportar: ${e.message}`);
  } finally {
    boton.disabled = false;
    boton.textContent = '⬇ Exportar ZIP';
  }
}

// ── Lanzar alerta ────────────────────────────────────────────────────────────

async function verAlertas() {
  $('#alEnviar').onclick = async () => {
    const opcion = $('#alUsuario').selectedOptions[0];
    if (!opcion) return;
    if (!confirm(`¿Lanzar «${TIPOS[$('#alTipo').value]}» a ${opcion.textContent}? Le llegará como una alerta real.`)) return;
    const res = $('#alResultado');
    res.textContent = 'Enviando…';
    try {
      const ev = await json(`${API}/events/simulate`, { method: 'POST', body: JSON.stringify({
        householdId: $('#alUsuario').value, eventType: $('#alTipo').value,
        cameraName: $('#alCamara').value.trim() || null }) });
      res.innerHTML = `${chip('Enviada', 'ok')} Evento creado ${fechaLima(ev.createdAt || new Date())}.`;
    } catch (e) {
      res.innerHTML = `${chip('Error', 'bad')} ${esc(e.message)}`;
    }
  };
}

// ── Servidores ───────────────────────────────────────────────────────────────

const PUNTOS = 200;   // 10 min a 3 s
const graficos = {};
function estiloEjes(max) {
  return { x: { ticks: { color: '#9CA3AF', maxTicksLimit: 6 }, grid: { color: '#1F2937' } },
    y: { min: 0, max, ticks: { color: '#9CA3AF' }, grid: { color: '#1F2937' } } };
}
function graficoVM(id) {
  if (graficos[id]) return graficos[id];
  graficos[id] = new Chart($(id), { type: 'line',
    data: { labels: [], datasets: [
      { label: 'CPU %', data: [], borderColor: '#00C2FF', backgroundColor: '#00C2FF22', fill: true, tension: .25, pointRadius: 0 },
      { label: 'RAM %', data: [], borderColor: '#F59E0B', tension: .25, pointRadius: 0 }] },
    options: { animation: false, scales: estiloEjes(100), plugins: { legend: { labels: { color: '#E5E7EB' } } } } });
  return graficos[id];
}
function agregarPunto(g, m) {
  g.data.labels.push(new Date().toLocaleTimeString('es-PE', { timeZone: 'America/Lima' }));
  g.data.datasets[0].data.push(m.cpu);
  g.data.datasets[1].data.push(m.ram);
  if (g.data.labels.length > PUNTOS) { g.data.labels.shift(); g.data.datasets.forEach((d) => d.data.shift()); }
  g.update();
}
const kpisVM = (m) => [kpi(`${m.cpu}%`, `CPU (${m.nucleos} núcleos)`), kpi(`${m.ram}%`, `RAM ${m.ramUsadaMb} / ${m.ramTotalMb} MB`),
  kpi((m.carga || []).map((x) => x.toFixed(2)).join(' · ') || '—', 'carga 1 · 5 · 15 min'),
  kpi(`${m.discoUsadoGb} / ${m.discoTotalGb} GB`, 'disco')].join('');

async function verServidores() {
  const az = graficoVM('#graficoAzure');
  const or = graficoVM('#graficoOracle');
  cada(3000, async () => {
    const [a, o] = await Promise.allSettled([json(`${IA}/admin/metricas`), json(`${API}/admin/metricas`)]);
    if (a.status === 'fulfilled') {
      agregarPunto(az, a.value);
      $('#kpiAzure').innerHTML = kpisVM(a.value);
      $('#tablaProcesos').innerHTML = a.value.procesos.map((p) => `<tr><td>${esc(p.nombre)}</td><td>${p.cpu}%</td>
        <td>${p.ramMb} MB</td><td class="envolver">${esc(p.cmd)}</td></tr>`).join('');
    } else { $('#kpiAzure').innerHTML = kpi('—', 'la VM de IA no responde (¿apagada?)'); }
    if (o.status === 'fulfilled') {
      agregarPunto(or, o.value);
      $('#kpiOracle').innerHTML = kpisVM(o.value) + kpi(`${o.value.backendRamMb} MB`, 'RAM del backend');
    }
  });
}

// ── Administradores ──────────────────────────────────────────────────────────

async function verAdmins() {
  const pintar = async () => {
    const lista = await json(`${API}/users/admins`);
    $('#tablaAdmins').innerHTML = lista.map((a) => `<tr><td>${esc(a.name)}</td><td>${esc(a.email)}</td>
      <td>${fechaLima(a.createdAt)}</td><td><button class="btn sec" data-quitar="${esc(a.id)}">Quitar acceso</button></td></tr>`).join('');
  };
  $('#adAgregar').onclick = async () => {
    $('#adError').textContent = '';
    try {
      await json(`${API}/users/admins`, { method: 'POST', body: JSON.stringify({ email: $('#adCorreo').value.trim() }) });
      $('#adCorreo').value = '';
      await pintar();
    } catch (e) { $('#adError').textContent = e.message; }
  };
  $('#tablaAdmins').onclick = async (e) => {
    const id = e.target.dataset.quitar;
    if (!id || !confirm('¿Quitar el acceso de administrador a esta cuenta?')) return;
    try { await pedir(`${API}/users/admins/${id}`, { method: 'DELETE' }); await pintar(); } catch (err) { $('#adError').textContent = err.message; }
  };
  await pintar();
}

// ── Modal ────────────────────────────────────────────────────────────────────

function abrirModal(titulo, html) {
  $('#modalTitulo').textContent = titulo;
  $('#modalCuerpo').innerHTML = html;
  $('#modal').classList.remove('oculto');
}
function cerrarModal() {
  temporizadorModal.forEach(clearInterval);
  temporizadorModal = [];
  $('#modalCuerpo').innerHTML = '';
  $('#modal').classList.add('oculto');
}
$('#modalCerrar').addEventListener('click', cerrarModal);
$('#modal').addEventListener('click', (e) => { if (e.target.id === 'modal') cerrarModal(); });
document.addEventListener('keydown', (e) => { if (e.key === 'Escape') cerrarModal(); });

if (token) entrar();
