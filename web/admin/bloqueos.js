/*
 * Sección «Cupo y bloqueos».
 *
 * - Cupo: cuántas cámaras analiza a la vez la VM de IA (cupos.py). Las que no
 *   entran esperan en cola y la app le muestra al usuario su posición.
 * - Bloqueos: cámara, cuenta (todas sus cámaras, presentes y futuras),
 *   teléfono (X-Device-Id) o IP. Duran hasta quitarlos aquí.
 * - Teléfonos vistos: los usados por más cuentas primero, para detectar multicuentas.
 *
 * Va en su propio archivo para no tocar admin.js: agrega su enlace y su
 * sección al cargar y se registra en SECCIONES.
 */
(() => {
  const TIPOS = { camara: 'Cámara', cuenta: 'Cuenta', dispositivo: 'Teléfono', ip: 'IP' };

  document.querySelector('nav.lateral .pie')?.insertAdjacentHTML('beforebegin',
    '<a href="#bloqueos" data-seccion="bloqueos">⛔ Cupo y bloqueos</a>');
  document.querySelector('main')?.insertAdjacentHTML('beforeend', `
    <section data-seccion="bloqueos" class="oculto">
      <h2>Cupo de cámaras</h2>
      <p class="sub">Cámaras que la VM de IA analiza a la vez. Las demás esperan en cola, en orden de llegada, y la app le muestra al usuario su posición y que el servidor está en alta demanda por la fase beta.</p>
      <div class="kpis" id="kpisCupo"></div>
      <div class="fila"><div><label for="maxCupo">Máximo simultáneo</label><input id="maxCupo" type="number" min="0" max="200" style="width:8em"></div>
        <div><label>&nbsp;</label><button class="btn" id="guardarCupo">Guardar</button></div></div>
      <div class="tabla"><table><thead><tr><th>Estado</th><th>Cámara</th><th>Id</th><th></th></tr></thead><tbody id="tablaCupo"></tbody></table></div>

      <h2>Bloqueos</h2>
      <p class="sub">Una cámara bloqueada no se analiza y su dueño no puede reactivarla. Bloquear una cuenta deshabilita todas las cámaras de su hogar, también las que agregue después. Un teléfono o una IP bloqueados deshabilitan toda cuenta que entre desde ahí. La app le dice al usuario que fue el administrador.</p>
      <div class="fila">
        <div><label for="tipoBloqueo">Tipo</label><select id="tipoBloqueo">${Object.entries(TIPOS).map(([k, v]) => `<option value="${k}">${v}</option>`).join('')}</select></div>
        <div class="crece"><label for="valorBloqueo">Valor (id, identificador del teléfono o IP)</label><input id="valorBloqueo"></div>
        <div class="crece"><label for="motivoBloqueo">Motivo (opcional)</label><input id="motivoBloqueo"></div>
        <div><label>&nbsp;</label><button class="btn" id="crearBloqueo">Bloquear</button></div>
      </div>
      <div class="tabla"><table><thead><tr><th>Tipo</th><th>Afecta a</th><th>Valor</th><th>Motivo</th><th>Desde</th><th></th></tr></thead><tbody id="tablaBloqueos"></tbody></table></div>

      <h2>Teléfonos vistos</h2>
      <p class="sub">Desde qué teléfono entra cada cuenta (la app 1.1.0 en adelante lo envía). Arriba, los que usan varias cuentas.</p>
      <div class="fila"><div class="crece"><label for="buscarDispositivo">Buscar</label><input id="buscarDispositivo" placeholder="Correo, identificador o IP"></div></div>
      <div class="tabla"><table><thead><tr><th>Teléfono</th><th>Cuentas</th><th>IPs</th><th>Última vez</th><th></th></tr></thead><tbody id="tablaDispositivos"></tbody></table></div>
    </section>`);

  const boton = (texto, datos) => `<button class="btn sec" ${Object.entries(datos).map(([k, v]) => `data-${k}="${esc(v)}"`).join(' ')}>${texto}</button>`;

  async function bloquear(tipo, valor, pedirMotivo = true) {
    const motivo = pedirMotivo ? prompt(`Motivo del bloqueo (${TIPOS[tipo]}):`, '') : '';
    if (motivo === null) return;
    await json(`${API}/admin/bloqueos`, { method: 'POST', body: JSON.stringify({ tipo, valor, motivo }) });
  }

  async function verBloqueos() {
    const pintarCupo = (c) => {
      $('#kpisCupo').innerHTML = [kpi(c.activos.length, 'analizándose'), kpi(c.max, 'máximo'), kpi(c.cola.length, 'en cola'), kpi(c.bloqueadas, 'bloqueadas')].join('');
      if (document.activeElement !== $('#maxCupo')) $('#maxCupo').value = c.max;
      const filas = [...c.activos, ...c.cola];
      $('#tablaCupo').innerHTML = filas.length ? filas.map((f) => `<tr>
        <td>${f.estado === 'activo' ? chip('Analizándose', 'ok') : chip(`En cola #${f.posicion}`, 'info')}</td>
        <td>${esc(f.nombre)}</td><td><small>${esc(f.id)}</small></td>
        <td>${boton('Bloquear cámara', { tipo: 'camara', valor: f.id })}</td></tr>`).join('')
        : '<tr><td colspan="4" class="vacio">Ninguna cámara con señal.</td></tr>';
    };
    const cargarCupo = async () => {
      try { pintarCupo(await json(`${IA}/admin/cupos`)); } catch (e) { $('#tablaCupo').innerHTML = `<tr><td colspan="4" class="vacio">VM de IA sin respuesta: ${esc(e.message)}</td></tr>`; }
    };
    const cargarBloqueos = async () => {
      const lista = await json(`${API}/admin/bloqueos`);
      $('#tablaBloqueos').innerHTML = lista.length ? lista.map((b) => `<tr>
        <td>${chip(TIPOS[b.tipo] || b.tipo, 'info')}</td><td>${esc(b.detalle || '—')}</td><td><small>${esc(b.valor)}</small></td>
        <td>${esc(b.motivo || '')}</td><td>${fechaLima(b.creadoEn)}</td>
        <td>${boton('Quitar', { quitar: b.id })}</td></tr>`).join('')
        : '<tr><td colspan="6" class="vacio">Sin bloqueos.</td></tr>';
    };
    const cargarDispositivos = async () => {
      const q = $('#buscarDispositivo').value.trim();
      const lista = await json(`${API}/admin/bloqueos/dispositivos${q ? `?buscar=${encodeURIComponent(q)}` : ''}`);
      $('#tablaDispositivos').innerHTML = lista.length ? lista.map((d) => `<tr>
        <td><small>${esc(d.deviceId)}</small><br>${esc(d.plataforma || '')}</td>
        <td>${d.cuentas.map((c) => `${esc(c.correo)} ${boton('Bloquear cuenta', { tipo: 'cuenta', valor: c.usuarioId })}`).join('<br>')}</td>
        <td>${d.ips.map((ip) => `${esc(ip)} ${boton('Bloquear IP', { tipo: 'ip', valor: ip })}`).join('<br>')}</td>
        <td>${fechaLima(d.ultimaVez)}</td>
        <td>${d.cuentas.length > 1 ? chip(`${d.cuentas.length} cuentas`, 'bad') : ''} ${boton('Bloquear teléfono', { tipo: 'dispositivo', valor: d.deviceId })}</td></tr>`).join('')
        : '<tr><td colspan="5" class="vacio">Sin teléfonos registrados todavía.</td></tr>';
    };
    const todo = () => Promise.all([cargarCupo(), cargarBloqueos(), cargarDispositivos()]);

    $('#guardarCupo').onclick = async () => {
      try { pintarCupo(await json(`${IA}/admin/cupos`, { method: 'POST', body: JSON.stringify({ max: Number($('#maxCupo').value) }) })); }
      catch (e) { alert(`No se pudo guardar: ${e.message}`); }
    };
    $('#crearBloqueo').onclick = async () => {
      try {
        await json(`${API}/admin/bloqueos`, { method: 'POST', body: JSON.stringify({ tipo: $('#tipoBloqueo').value, valor: $('#valorBloqueo').value.trim(), motivo: $('#motivoBloqueo').value.trim() }) });
        $('#valorBloqueo').value = ''; $('#motivoBloqueo').value = '';
        await todo();
      } catch (e) { alert(`No se pudo bloquear: ${e.message}`); }
    };
    let espera;
    $('#buscarDispositivo').oninput = () => { clearTimeout(espera); espera = setTimeout(cargarDispositivos, 400); };
    $('section[data-seccion="bloqueos"]').onclick = async (e) => {
      const b = e.target.closest('button[data-tipo], button[data-quitar]');
      if (!b) return;
      try {
        if (b.dataset.quitar) {
          if (!confirm('¿Quitar este bloqueo?')) return;
          await pedir(`${API}/admin/bloqueos/${b.dataset.quitar}`, { method: 'DELETE' });
        } else {
          await bloquear(b.dataset.tipo, b.dataset.valor);
        }
        await todo();
      } catch (err) { alert(`No se pudo: ${err.message}`); }
    };
    cada(15000, todo);
  }

  SECCIONES.bloqueos = verBloqueos;
  if (location.hash === '#bloqueos') irA();
})();
