const me = Auth.require('Admin');
if (me) document.getElementById('userName').textContent = me.fullName;

const $ = id => document.getElementById(id);
let doctors = [], patients = [], appointments = [], editDoctorId = null, dash = null;
const SC = { Scheduled: '#3498DB', InProgress: '#F39C12', Completed: '#27AE60', Cancelled: '#E74C3C' };

['doctorsTable', 'patientsTable', 'apptTable', 'ratingsTable', 'specTable'].forEach(id => makeSortable($(id)));
initTabs(tab => { if (tab === 'stats') loadStats(); });

async function loadAll() {
  try {
    [doctors, patients, dash] = await Promise.all([api('/doctors'), api('/patients'), api('/statistics/dashboard')]);
    renderDashboard(); renderDoctors(); renderPatients(); renderRatings(); fillDoctorFilter();
    await loadAppointments();
  } catch (e) { toast(e.message, 'error'); }
}

/* ================= Дашборд ================= */
function renderDashboard() {
  const items = [
    ['Врачей', dash.totalDoctors, ''], ['Работают', dash.activeDoctors, 'green'], ['Пациентов', dash.totalPatients, ''],
    ['Всего записей', dash.totalAppointments, ''], ['Записей сегодня', dash.todayAppointments, 'orange'], ['Отзывов', dash.totalReviews, 'red']
  ];
  $('statsGrid').innerHTML = items.map(([l, n, c]) => `<div class="stat ${c}"><div class="num">${n}</div><div class="lbl">${l}</div></div>`).join('');
  const keys = Object.keys(dash.byStatus);
  makeChart('chStatus', () => ({ type: 'doughnut', data: { labels: keys.map(k => (STATUS[k] || [k])[0]),
    datasets: [{ data: keys.map(k => dash.byStatus[k]), backgroundColor: keys.map(k => SC[k] || '#999') }] },
    options: { responsive: true, maintainAspectRatio: false } }));
  makeChart('chRatings', () => ({ type: 'bar', data: { labels: dash.doctorRatings.map(d => d.fullName),
    datasets: [{ label: 'Средняя оценка', data: dash.doctorRatings.map(d => d.average), backgroundColor: '#F39C12' }] },
    options: { responsive: true, maintainAspectRatio: false, scales: { y: { min: 0, max: 5 } } } }));
}

/* ================= Врачи (сотрудники) ================= */
function renderDoctors() {
  const q = $('docSearch').value.trim().toLowerCase();
  const list = doctors.filter(d => [d.fullName, d.specialization, d.phoneNumber, d.email, d.cabinetNumber].join(' ').toLowerCase().includes(q));
  $('doctorsBody').innerHTML = list.length ? list.map(d => `
    <tr><td><b>${esc(d.fullName)}</b><br><span class="muted">${d.birthDate ? fmtDate(d.birthDate) : ''}${d.gender ? ' · ' + esc(d.gender) : ''}</span></td>
    <td>${esc(d.specialization)}${d.category ? `<br><span class="muted">${esc(d.category)} категория</span>` : ''}</td>
    <td>${esc(d.cabinetNumber || '—')}</td>
    <td>${esc(d.phoneNumber || '—')}<br><span class="muted">${esc(d.email || '')}</span></td>
    <td data-v="${d.experienceYears ?? -1}">${d.experienceYears != null ? d.experienceYears + ' л.' : '—'}</td>
    <td data-v="${d.rating || 0}">${starsHtml(d.rating, d.reviewsCount)}</td>
    <td>${d.isActive ? '<span class="badge badge-green">Работает</span>' : '<span class="badge badge-gray">Не принимает</span>'}</td>
    <td><button class="btn btn-light btn-sm" data-act="edit" data-id="${d.id}"><i class="fa-solid fa-pen"></i>Изменить</button>
        <button class="btn btn-danger btn-sm" data-act="del" data-id="${d.id}"><i class="fa-solid fa-trash"></i></button></td></tr>`).join('')
    : '<tr><td colspan="8" class="empty">Сотрудников не найдено</td></tr>';
}
$('docSearch').addEventListener('input', renderDoctors);
$('doctorsBody').addEventListener('click', async e => {
  const b = e.target.closest('button'); if (!b) return;
  const id = +b.dataset.id;
  if (b.dataset.act === 'edit') openDoctorForm(doctors.find(d => d.id === id));
  if (b.dataset.act === 'del') {
    if (!confirm('Удалить сотрудника?')) return;
    try { const r = await api('/doctors/' + id, { method: 'DELETE' }); toast(r.message, r.deactivated ? 'warning' : 'success'); loadAll(); }
    catch (err) { toast(err.message, 'error'); }
  }
});
$('btnAddDoctor').addEventListener('click', () => openDoctorForm(null));

const dateInput = s => s ? String(s).slice(0, 10) : '';
function openDoctorForm(d) {
  editDoctorId = d ? d.id : null;
  $('doctorModalTitle').innerHTML = d ? '<i class="fa-solid fa-user-pen"></i>Карточка сотрудника' : '<i class="fa-solid fa-user-plus"></i>Новый сотрудник';
  const parts = (d?.fullName || '').split(/\s+/).filter(Boolean);
  $('dLast').value = parts[0] || ''; $('dFirst').value = parts[1] || ''; $('dMiddle').value = parts.slice(2).join(' ');
  $('dBirth').value = dateInput(d?.birthDate); $('dGender').value = d?.gender || '';
  $('dPhone').value = d?.phoneNumber || ''; $('dEmail').value = d?.email || ''; $('dAddress').value = d?.address || '';
  $('dSpec').value = d?.specialization || ''; $('dCabinet').value = d?.cabinetNumber || '';
  $('dCategory').value = d?.category || ''; $('dExp').value = d?.experienceYears ?? ''; $('dHire').value = dateInput(d?.hireDate);
  $('dEdu').value = d?.education || ''; $('dActive').checked = d ? d.isActive : true;
  $('dLogin').value = ''; $('dPass').value = ''; $('dPass2').value = ''; $('dLogin').disabled = !!d;
  $('dLoginNote').textContent = d ? '(не меняется)' : '*'; $('dPassNote').textContent = d ? '(пусто — не менять)' : '*';
  document.querySelectorAll('#doctorForm .invalid').forEach(x => x.classList.remove('invalid'));
  openModal('doctorModal');
}
$('doctorForm').addEventListener('submit', async e => {
  e.preventDefault();
  const req = ['dLast', 'dFirst', 'dSpec', 'dPhone'].concat(editDoctorId ? [] : ['dLogin', 'dPass']);
  if (!validate(req)) return;
  const email = val('dEmail');
  if (email && !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)) { $('dEmail').classList.add('invalid'); toast('Проверьте email', 'warning'); return; }
  if ($('dPass').value !== $('dPass2').value) { $('dPass2').classList.add('invalid'); toast('Пароли не совпадают', 'warning'); return; }
  const body = {
    login: val('dLogin') || null, password: $('dPass').value || null,
    fullName: [val('dLast'), val('dFirst'), val('dMiddle')].filter(Boolean).join(' '),
    birthDate: nullIfEmpty(val('dBirth')), gender: nullIfEmpty(val('dGender')), specialization: val('dSpec'),
    category: nullIfEmpty(val('dCategory')), experienceYears: nullIfEmpty(val('dExp')) === null ? null : +val('dExp'),
    education: nullIfEmpty(val('dEdu')), hireDate: nullIfEmpty(val('dHire')), cabinetNumber: nullIfEmpty(val('dCabinet')),
    phoneNumber: nullIfEmpty(val('dPhone')), email: nullIfEmpty(email), address: nullIfEmpty(val('dAddress')), isActive: $('dActive').checked
  };
  try {
    if (editDoctorId) await api('/doctors/' + editDoctorId, { method: 'PUT', body }); else await api('/doctors', { method: 'POST', body });
    closeModal('doctorModal'); toast(editDoctorId ? 'Данные сотрудника сохранены' : 'Сотрудник добавлен — он может войти под своим логином'); loadAll();
  } catch (err) { toast(err.message, 'error'); }
});

/* ================= Пациенты ================= */
function renderPatients() {
  const q = $('pSearch').value.trim().toLowerCase(), d = $('pDate').value;
  const list = patients.filter(p => [p.fullName, p.phoneNumber, p.policyNumber].join(' ').toLowerCase().includes(q)
    && (!d || (p.createdAt || '').slice(0, 10) === d));
  $('patientsBody').innerHTML = list.length ? list.map(p => `
    <tr><td>${esc(p.fullName)}</td><td>${fmtDate(p.birthDate)}</td><td>${esc(p.gender || '—')}</td><td>${esc(p.phoneNumber || '—')}</td>
    <td>${esc(p.policyNumber || '—')}</td><td>${esc(p.email || '—')}</td><td>${esc(p.address || '—')}</td><td>${fmtDate(p.createdAt)}</td>
    <td><button class="btn btn-danger btn-sm" data-id="${p.id}"><i class="fa-solid fa-trash"></i></button></td></tr>`).join('')
    : '<tr><td colspan="9" class="empty">Пациентов не найдено</td></tr>';
}
['pSearch', 'pDate'].forEach(id => $(id).addEventListener('input', renderPatients));
$('pReset').addEventListener('click', () => { $('pSearch').value = $('pDate').value = ''; renderPatients(); });
$('patientsBody').addEventListener('click', async e => {
  const b = e.target.closest('button'); if (!b) return;
  if (!confirm('Удалить пациента вместе с записями и историей?')) return;
  try { await api('/patients/' + b.dataset.id, { method: 'DELETE' }); toast('Пациент удалён'); loadAll(); }
  catch (err) { toast(err.message, 'error'); }
});

/* ================= Записи ================= */
function fillDoctorFilter() {
  const cur = $('fDoctor').value;
  $('fDoctor').innerHTML = '<option value="">Все врачи</option>' + doctors.map(d => `<option value="${d.id}">${esc(d.fullName)}</option>`).join('');
  $('fDoctor').value = cur;
}
async function loadAppointments() {
  const p = new URLSearchParams();
  if (val('fDate')) p.set('date', val('fDate')); if (val('fDoctor')) p.set('doctorId', val('fDoctor')); if (val('fStatus')) p.set('status', val('fStatus'));
  try { appointments = await api('/appointments?' + p, { silent: true }); renderAppts(); } catch (e) { toast(e.message, 'error'); }
}
['fDate', 'fDoctor', 'fStatus'].forEach(id => $(id).addEventListener('change', loadAppointments));
$('fSearch').addEventListener('input', renderAppts);
$('fReset').addEventListener('click', () => { ['fDate', 'fDoctor', 'fStatus', 'fSearch'].forEach(id => $(id).value = ''); loadAppointments(); });

function renderAppts() {
  const q = $('fSearch').value.trim().toLowerCase();
  const list = appointments.filter(a => (a.patientName || '').toLowerCase().includes(q));
  $('apptBody').innerHTML = list.length ? list.map(a => `
    <tr class="clickable" data-id="${a.id}"><td>${fmtDateTime(a.appointmentDate)}</td><td>${a.queueNumber}</td><td>${esc(a.patientName)}</td>
    <td>${esc(a.doctorName)}<br><span class="muted">${esc(a.specialization || '')}</span></td><td>${statusBadge(a.status)}</td>
    <td>${a.status === 'Scheduled' ? `<button class="btn btn-light btn-sm" data-act="cancel" data-id="${a.id}"><i class="fa-solid fa-ban"></i>Отменить</button>` : ''}
        <button class="btn btn-danger btn-sm" data-act="del" data-id="${a.id}"><i class="fa-solid fa-trash"></i></button></td></tr>`).join('')
    : '<tr><td colspan="6" class="empty">Записей не найдено</td></tr>';
}
$('apptBody').addEventListener('click', async e => {
  const b = e.target.closest('button');
  if (!b) { const row = e.target.closest('tr.clickable'); if (row) showAppointmentDetails(+row.dataset.id); return; }
  try {
    if (b.dataset.act === 'cancel') await api(`/appointments/${b.dataset.id}/status`, { method: 'PUT', body: { status: 'Cancelled' } });
    else { if (!confirm('Удалить запись?')) return; await api('/appointments/' + b.dataset.id, { method: 'DELETE' }); }
    toast('Готово'); loadAll();
  } catch (err) { toast(err.message, 'error'); }
});

/* ================= Рейтинги ================= */
function renderRatings() {
  $('ratingsBody').innerHTML = dash.doctorRatings.length ? dash.doctorRatings.map(d => `
    <tr class="clickable" data-id="${d.doctorId}" data-name="${esc(d.fullName)}"><td>${esc(d.fullName)}</td><td>${esc(d.specialization)}</td>
    <td data-v="${d.average}">${starsHtml(d.average)} <b>${d.count ? d.average.toFixed(2) : '—'}</b></td><td>${d.count}</td></tr>`).join('')
    : '<tr><td colspan="4" class="empty">Врачей нет</td></tr>';
}
$('ratingsBody').addEventListener('click', async e => {
  const row = e.target.closest('tr.clickable'); if (!row) return;
  $('revTitle').textContent = 'Отзывы: ' + row.dataset.name; $('revBody').innerHTML = '<div class="empty"><div class="spinner small"></div></div>'; openModal('reviewsModal');
  try {
    const r = await api('/reviews/doctor/' + row.dataset.id);
    $('revBody').innerHTML = `<p>${starsHtml(r.average, r.count)}</p>` + (r.reviews.length ? r.reviews.map(x => `
      <div class="review"><div class="toolbar" style="margin:0"><b>${esc(x.patientName)}</b><span class="muted">${fmtDate(x.createdAt)}</span></div>
      ${starsHtml(x.rating)}<p style="margin:4px 0">${esc(x.comment || 'Без комментария')}</p></div>`).join('') : '<div class="empty">Отзывов пока нет</div>');
  } catch (err) { $('revBody').innerHTML = `<div class="alert alert-error">${esc(err.message)}</div>`; }
});

/* ================= Статистика ================= */
async function loadStats() {
  try {
    const [days, specs] = await Promise.all([api('/statistics/patients-by-day?days=' + $('daysSel').value), api('/statistics/specializations')]);
    makeChart('chDays', () => ({ type: 'line', data: { labels: days.map(d => fmtDate(d.date + 'T00:00:00').slice(0, 5)),
      datasets: [{ label: 'Приёмы', data: days.map(d => d.appointments), borderColor: '#3498DB', backgroundColor: 'rgba(52,152,219,.15)', fill: true, tension: .3 },
                 { label: 'Уникальные пациенты', data: days.map(d => d.patients), borderColor: '#27AE60', tension: .3 }] },
      options: { responsive: true, maintainAspectRatio: false, scales: { y: { beginAtZero: true, ticks: { precision: 0 } } } } }));
    makeChart('chSpec', () => ({ type: 'bar', data: { labels: specs.map(s => s.specialization),
      datasets: [{ label: 'Записей', data: specs.map(s => s.appointments), backgroundColor: '#3498DB' }] },
      options: { responsive: true, maintainAspectRatio: false, scales: { y: { beginAtZero: true, ticks: { precision: 0 } } } } }));
    $('specBody').innerHTML = specs.map(s => `<tr><td>${esc(s.specialization)}</td><td>${s.doctors}</td><td>${s.appointments}</td></tr>`).join('') || '<tr><td colspan="3" class="empty">Нет данных</td></tr>';
  } catch (e) { toast(e.message, 'error'); }
}
$('daysSel').addEventListener('change', loadStats);

loadAll();
