/*
 * Sección «Evidencia»: interruptor «Grabar evidencia» por hogar.
 *
 * Si está encendido, el servicio vigishield-grabaciones (VM de IA) guarda el
 * primer minuto de cada transmisión desde las cámaras de celular del hogar,
 * una vez al día. Va en su propio archivo para no tocar admin.js: usa sus
 * funciones (json, esc, chip, kpi, fechaLima, cada) y se registra en SECCIONES.
 */
(() => {
  let hogares = [];
  let politicaDesde = null;

  async function verEvidencia() {
    const pintar = () => {
      const q = $('#buscarEvidencia').value.trim().toLowerCase();
      const lista = hogares.filter((h) => !q || `${h.dueno} ${h.correo}`.toLowerCase().includes(q));
      $('#kpisEvidencia').innerHTML = [
        kpi(hogares.filter((h) => h.grabar).length, 'hogares que se graban'),
        kpi(hogares.filter((h) => h.grabar && h.camarasCelular > 0).length, 'con cámara de celular'),
        kpi(hogares.filter((h) => h.manual).length, 'cambiados a mano'),
      ].join('');
      $('#politicaEvidencia').textContent = politicaDesde ? fechaLima(politicaDesde) : '—';
      $('#tablaEvidencia').innerHTML = lista.length ? lista.map((h) => `<tr>
        <td><label class="sw" title="Grabar evidencia"><input type="checkbox" data-hogar="${esc(h.hogarId)}" ${h.grabar ? 'checked' : ''}><span></span></label></td>
        <td>${esc(h.dueno)}<br><small style="color:var(--muted)">${esc(h.correo)}</small></td>
        <td>${fechaLima(h.registrado)}</td>
        <td>${h.terminosAceptados ? fechaLima(h.terminosAceptados) : '—'}</td>
        <td>${h.camarasCelular}</td>
        <td>${h.manual ? chip('A mano', 'info') : h.grabar ? chip('Política nueva', 'ok') : chip('Cuenta anterior')}</td>
      </tr>`).join('') : '<tr><td colspan="6" class="vacio">Sin hogares para esta búsqueda.</td></tr>';
    };
    $('#buscarEvidencia').oninput = pintar;
    $('#tablaEvidencia').onchange = async (e) => {
      const id = e.target.dataset.hogar;
      if (!id) return;
      e.target.disabled = true;
      try {
        const h = await json(`${API}/admin/evidencia/${id}`, { method: 'PUT', body: JSON.stringify({ grabar: e.target.checked }) });
        hogares = hogares.map((x) => (x.hogarId === h.hogarId ? h : x));
        pintar();
      } catch (err) {
        e.target.checked = !e.target.checked;
        alert(`No se pudo cambiar: ${err.message}`);
      } finally { e.target.disabled = false; }
    };
    cada(30000, async () => {
      const estado = await json(`${API}/admin/evidencia`);
      hogares = estado.hogares;
      politicaDesde = estado.politicaDesde;
      pintar();
    });
  }

  SECCIONES.evidencia = verEvidencia;
  // Si el panel se abrió directo en #evidencia, admin.js aún no conocía la sección.
  if (location.hash === '#evidencia') irA();
})();
