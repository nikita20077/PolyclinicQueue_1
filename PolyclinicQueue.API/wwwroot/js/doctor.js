const me = Auth.require('Doctor');
if (me) document.getElementById('userName').textContent = me.fullName;

const $ = id => document.getElementById(id);
let queue = [], upcomingList = [], current = null, currentPatient = null, myDoc = null, lastQueueCount = null;
let filePatients = [], fileSpec = '';

/* Частые диагнозы МКБ-10 для подсказок */
const MKB10 = [
  'A09 — Инфекционный гастроэнтерит и колит', 'B34.9 — Вирусная инфекция неуточнённая', 'E11 — Сахарный диабет 2 типа',
  'E66 — Ожирение', 'E03.9 — Гипотиреоз неуточнённый', 'F41.2 — Смешанное тревожное и депрессивное расстройство',
  'G43 — Мигрень', 'G44.2 — Головная боль напряжения', 'G47.0 — Нарушения сна (бессонница)', 'H10 — Конъюнктивит',
  'H66.9 — Средний отит неуточнённый', 'I10 — Эссенциальная (первичная) гипертензия', 'I20 — Стенокардия',
  'I25.1 — Атеросклеротическая болезнь сердца', 'I50 — Сердечная недостаточность', 'I83 — Варикозное расширение вен нижних конечностей',
  'J00 — Острый назофарингит (насморк)', 'J01 — Острый синусит', 'J02 — Острый фарингит', 'J03 — Острый тонзиллит',
  'J06.9 — Острая инфекция верхних дыхательных путей неуточнённая', 'J11 — Грипп, вирус не идентифицирован',
  'J18.9 — Пневмония неуточнённая', 'J20 — Острый бронхит', 'J30 — Вазомоторный и аллергический ринит',
  'J45 — Бронхиальная астма', 'K02.0 — Кариес эмали', 'K02.1 — Кариес дентина', 'K04.0 — Пульпит', 'K04.5 — Хронический периодонтит',
  'K05.1 — Хронический гингивит', 'K05.3 — Хронический пародонтит', 'K08.1 — Потеря зубов вследствие несчастного случая, удаления или пародонтита',
  'K21 — Гастроэзофагеальный рефлюкс', 'K29 — Гастрит и дуоденит', 'K30 — Диспепсия', 'K59.0 — Запор', 'K80 — Желчнокаменная болезнь',
  'L20 — Атопический дерматит', 'L23 — Аллергический контактный дерматит', 'L50 — Крапивница', 'M42 — Остеохондроз позвоночника',
  'M54.5 — Боль внизу спины', 'M54.2 — Цервикалгия', 'M17 — Гонартроз (артроз коленного сустава)', 'N10 — Острый пиелонефрит',
  'N30 — Цистит', 'R05 — Кашель', 'R07.4 — Боль в грудной клетке неуточнённая', 'R10 — Боли в области живота',
  'R50.9 — Лихорадка неуточнённая', 'R51 — Головная боль', 'S93 — Растяжение связок голеностопного сустава',
  'T78.4 — Аллергия неуточнённая', 'Z00.0 — Общий медицинский осмотр', 'Z01.2 — Осмотр зубов', 'Z02.7 — Выдача медицинской справки'
];
$('mkbList').innerHTML = MKB10.map(x => `<option value="${esc(x)}"></option>`).join('');

requestNotifications();
initTabs(tab => {
  if (tab === 'file') loadFile();
  if (tab === 'stats') loadStats();
  if (tab === 'reviews') loadReviews();
});

/* ================= Планировщик «следующий приём» (используется в двух местах) ================= */
class Scheduler {
  constructor(prefix, onBooked) {
    this.cal = $(prefix + 'Cal'); this.slots = $(prefix + 'Slots'); this.btn = $(prefix + 'Btn'); this.lbl = $(prefix + 'Lbl');
    this.date = todayStr(); this.time = null; this.patientId = 0; this.onBooked = onBooked;
    this.slots.addEventListener('click', e => {
      const b = e.target.closest('.slot'); if (!b || b.disabled) return;
      this.slots.querySelectorAll('.slot').forEach(s => s.classList.remove('selected'));
      b.classList.add('selected'); this.time = b.dataset.time; this.btn.disabled = false;
    });
    this.btn.addEventListener('click', () => this.book());
  }
  setPatient(id) {
    this.patientId = id; this.time = null; this.btn.disabled = true;
    renderCalendar(this.cal, this.date, d => { this.date = d; this.load(); }, 180);
    this.load();
  }
  async load() {
    this.time = null; this.btn.disabled = true;
    this.lbl.textContent = '· ' + fmtDate(this.date + 'T00:00:00');
    let busy = [];
    try { busy = await api(`/appointments/busy/${me.profileId}?date=${this.date}`, { silent: true }); } catch (e) { toast(e.message, 'error'); }
    const now = new Date(), html = [];
    for (let h = 9; h < 18; h++) for (const m of ['00', '30']) {
      const t = `${pad(h)}:${m}`;
      html.push(`<button class="slot" data-time="${t}" ${busy.includes(t) || new Date(`${this.date}T${t}:00`) <= now ? 'disabled' : ''}>${t}</button>`);
    }
    this.slots.innerHTML = html.join('');
  }
  async book() {
    if (!this.patientId || !this.time) { toast('Выберите дату и время', 'warning'); return; }
    try {
      await api('/appointments', { method: 'POST', body: { patientId: this.patientId, doctorId: me.profileId, appointmentDate: `${this.date}T${this.time}:00` } });
      toast(`Пациент записан на ${fmtDate(this.date + 'T00:00:00')} в ${this.time}`);
      await this.load(); this.onBooked && this.onBooked();
    } catch (e) { toast(e.message, 'error'); this.load(); }
  }
}
const rcSched = new Scheduler('rc', () => { loadQueue(true); loadUpcoming(); loadFileTimelineIfOpen(); });
const fcSched = new Scheduler('fc', () => { loadQueue(true); loadUpcoming(); loadFile(true); if (filePatientId) openFilePatient(filePatientId, true); });

/* Книжка обращений (общий рендер): приём, диагноз, назначения */
function bookHtml(items) {
  const color = { Completed: 'green', Cancelled: 'red', InProgress: 'orange', Scheduled: '' };
  return items.length ? items.map(t => `
    <div class="tl-item"><div class="tl-dot ${color[t.status] || ''}"></div>
    <div class="tl-card" style="cursor:default"><div class="tl-head"><b>${fmtDateTime(t.appointmentDate)}</b>${statusBadge(t.status)}</div>
    <div class="muted">${esc(t.doctorName)} · ${esc(t.specialization || '')}</div>
    ${t.diagnosis ? `<p><b>Диагноз:</b> ${esc(t.diagnosis)}</p><p><b>Назначения:</b> ${esc(t.prescriptions || '—')}</p><p><b>Рекомендации:</b> ${esc(t.recommendations || '—')}</p>`
      : `<p class="muted">${t.status === 'Scheduled' ? 'Приём ещё не состоялся' : t.status === 'Cancelled' ? 'Запись отменена' : 'Диагноз не внесён'}</p>`}</div></div>`).join('')
    : '<div class="muted">Обращений пока нет</div>';
}

/* ================= Очередь и предстоящие записи ================= */
async function loadQueue(silent = false) {
  try {
    queue = await api('/appointments/today/' + me.profileId, { silent });
    if (lastQueueCount !== null && queue.length > lastQueueCount) notify('Новый пациент в очереди', queue[queue.length - 1].patientName);
    lastQueueCount = queue.length;
    const fresh = current && queue.find(a => a.id === current.id);
    if (fresh) { current = fresh; updateButtons(); }
    renderQueue();
  } catch (e) { toast(e.message, 'error'); }
}
async function loadUpcoming() {
  try { upcomingList = await api('/appointments/upcoming/' + me.profileId, { silent: true }); renderUpcoming(); } catch { /* не критично */ }
}
const queueItem = (a, showDate) => `
  <div class="queue-item ${current && current.id === a.id ? 'active' : ''}" data-id="${a.id}">
    <div class="qn">${a.queueNumber}</div>
    <div style="flex:1"><b>${esc(a.patientName)}</b><br><span class="muted"><i class="fa-regular fa-clock"></i>${showDate ? fmtDateTime(a.appointmentDate) : fmtTime(a.appointmentDate)}</span></div>
    ${showDate ? '' : statusBadge(a.status)}</div>`;
function renderQueue() {
  $('queueList').innerHTML = queue.length ? queue.map(a => queueItem(a, false)).join('') : '<div class="card empty">На сегодня записей нет</div>';
  renderUpcoming();
}
function renderUpcoming() {
  $('upcomingList').innerHTML = upcomingList.length ? upcomingList.map(a => queueItem(a, true)).join('') : '<div class="muted">Предстоящих записей нет</div>';
}
document.addEventListener('click', e => {
  const item = e.target.closest('#queueList .queue-item, #upcomingList .queue-item'); if (!item) return;
  const id = +item.dataset.id;
  openAppointment(queue.find(a => a.id === id) || upcomingList.find(a => a.id === id));
});
$('btnRefresh').addEventListener('click', () => { loadQueue(); loadUpcoming(); });

/* ================= Карточка пациента на приёме ================= */
async function openAppointment(a) {
  if (!a) return;
  current = a; currentPatient = null; renderQueue();
  $('noSelection').classList.add('hidden'); $('patientPanel').classList.remove('hidden');
  $('pName').textContent = a.patientName;
  ['diagnosis', 'prescriptions', 'recommendations'].forEach(id => $(id).value = '');
  ['allergyBanner', 'flagBanners'].forEach(id => $(id).innerHTML = '');
  $('bmiH').value = $('bmiW').value = ''; $('bmiRes').textContent = '—';
  updateButtons(); rcSched.setPatient(a.patientId);

  try {
    const [patient, q, timeline] = await Promise.all([
      api('/patients/' + a.patientId), api('/questionnaires/' + a.id, { allow404: true }), api(`/patients/${a.patientId}/timeline`)
    ]);
    currentPatient = patient;
    $('pInfo').innerHTML = infoRows([['Дата рождения', patient.birthDate ? `${fmtDate(patient.birthDate)} (${ageOf(patient.birthDate)} лет)` : null],
      ['Пол', patient.gender], ['Телефон', patient.phoneNumber], ['Полис', patient.policyNumber], ['Email', patient.email], ['Адрес', patient.address]]);

    // красная плашка аллергий + красные флаги
    if (q && !isNone(q.allergies))
      $('allergyBanner').innerHTML = `<div class="banner red"><i class="fa-solid fa-triangle-exclamation"></i><div>АЛЛЕРГИЯ<small>${esc(q.allergies)}</small></div></div>`;
    $('flagBanners').innerHTML = q ? flagsHtml(analyzeFlags(`${q.complaints} ${q.chronicDiseases}`)) : '';
    $('qInfo').innerHTML = q ? infoRows([['Жалобы', q.complaints], ['Хронические заболевания', q.chronicDiseases], ['Аллергии', q.allergies], ['Принимаемые лекарства', q.medications]])
      : '<div class="muted">Пациент не заполнил анкету</div>';

    $('specPill').textContent = timeline[0]?.specialization || myDoc?.specialization || '';
    $('timeline').innerHTML = bookHtml(timeline);

    // если приём уже сохранён — подставляем в форму
    const mine = timeline.find(t => t.appointmentId === a.id && t.diagnosis);
    if (mine) { $('diagnosis').value = mine.diagnosis; $('prescriptions').value = mine.prescriptions || ''; $('recommendations').value = mine.recommendations || ''; }
    updateButtons();
  } catch (e) { toast(e.message, 'error'); }
}
async function loadFileTimelineIfOpen() {
  if (!current) return;
  try { $('timeline').innerHTML = bookHtml(await api(`/patients/${current.patientId}/timeline`, { silent: true })); } catch { /* ignore */ }
}

/* BMI */
function calcBmi() {
  const h = +$('bmiH').value / 100, w = +$('bmiW').value;
  if (!h || !w) { $('bmiRes').textContent = '—'; return; }
  const b = w / (h * h);
  const cat = b < 18.5 ? 'недостаточная масса' : b < 25 ? 'норма' : b < 30 ? 'избыточная масса' : 'ожирение';
  $('bmiRes').textContent = `${b.toFixed(1)} — ${cat}`;
}
$('bmiH').addEventListener('input', calcBmi); $('bmiW').addEventListener('input', calcBmi);

/* ================= Приём: начать / завершить / рецепт ================= */
function updateButtons() {
  if (!current) return;
  $('pStatus').innerHTML = statusBadge(current.status);
  const today = isToday(current.appointmentDate);
  $('btnStart').disabled = current.status !== 'Scheduled' || !today;
  $('btnFinish').disabled = current.status !== 'InProgress';
  $('btnRx').disabled = current.status !== 'Completed';
  $('startNote').textContent = current.status === 'Scheduled' && !today ? `Приём назначен на ${fmtDateTime(current.appointmentDate)} — начать его можно в день приёма.` : '';
  const lock = current.status === 'Completed' || current.status === 'Cancelled';
  ['diagnosis', 'prescriptions', 'recommendations'].forEach(id => $(id).disabled = lock);
}
async function setStatus(status) {
  await api(`/appointments/${current.id}/status`, { method: 'PUT', body: { status } });
  current.status = status; updateButtons();
}
$('btnStart').addEventListener('click', async () => {
  try { await setStatus('InProgress'); toast('Приём начат'); loadQueue(true); } catch (e) { toast(e.message, 'error'); }
});
$('btnFinish').addEventListener('click', async () => {
  if (!validate(['diagnosis'])) return;
  try {
    await api('/medicalrecords', { method: 'POST', body: { appointmentId: current.id, diagnosis: val('diagnosis'),
      prescriptions: nullIfEmpty(val('prescriptions')), recommendations: nullIfEmpty(val('recommendations')) } });
    await setStatus('Completed');
    toast('Диагноз записан, приём завершён. Теперь можно выписать рецепт или записать на следующий приём');
    loadQueue(true); loadFileTimelineIfOpen();
  } catch (e) { toast(e.message, 'error'); }
});
$('btnRx').addEventListener('click', () => {
  downloadPrescription({ patientName: current.patientName, birthDate: currentPatient?.birthDate, diagnosis: val('diagnosis'),
    prescriptions: val('prescriptions'), recommendations: val('recommendations'), doctorName: me.fullName,
    specialization: myDoc?.specialization, date: new Date() });
});

/* ================= Картотека пациентов (по специальности врача) ================= */
let filePatientId = 0;
async function loadFile(silent = false) {
  try {
    const r = await api('/patients/my', { silent });
    fileSpec = r.specialization; filePatients = r.patients;
    $('fileHint').textContent = `Здесь только пациенты, которые обращались к врачам специальности «${fileSpec}».`;
    renderFileList();
  } catch (e) { toast(e.message, 'error'); }
}
function renderFileList() {
  const q = $('fSearch').value.trim().toLowerCase();
  const list = filePatients.filter(p => [p.fullName, p.phoneNumber, p.policyNumber].join(' ').toLowerCase().includes(q));
  $('fList').innerHTML = list.length ? list.map(p => `
    <div class="queue-item ${p.id === filePatientId ? 'active' : ''}" data-id="${p.id}">
      <div class="avatar-sm">${esc(p.fullName[0] || '?')}</div>
      <div style="flex:1"><b>${esc(p.fullName)}</b><br>
        <span class="muted" style="font-size:12px">Приёмов: ${p.visits}${p.lastVisit ? ' · последний ' + fmtDate(p.lastVisit) : ''}</span>
        ${p.nextVisit ? `<br><span class="pill"><i class="fa-regular fa-calendar" style="margin-right:3px"></i>следующий ${fmtDateTime(p.nextVisit)}</span>` : ''}</div></div>`).join('')
    : '<div class="card empty">Пациентов не найдено</div>';
}
$('fSearch').addEventListener('input', renderFileList);
$('fList').addEventListener('click', e => { const it = e.target.closest('.queue-item'); if (it) openFilePatient(+it.dataset.id); });

async function openFilePatient(id, keepScheduler = false) {
  filePatientId = id; renderFileList();
  $('fEmpty').classList.add('hidden'); $('fPanel').classList.remove('hidden');
  try {
    const [p, timeline] = await Promise.all([api('/patients/' + id, { silent: keepScheduler }), api(`/patients/${id}/timeline`, { silent: keepScheduler })]);
    $('fName').textContent = p.fullName;
    $('fInfo').innerHTML = infoRows([['Дата рождения', p.birthDate ? `${fmtDate(p.birthDate)} (${ageOf(p.birthDate)} лет)` : null],
      ['Пол', p.gender], ['Телефон', p.phoneNumber], ['Полис', p.policyNumber], ['Email', p.email], ['Адрес', p.address]]);
    $('fSpecPill').textContent = fileSpec;
    $('fTimeline').innerHTML = bookHtml(timeline);
    if (!keepScheduler) fcSched.setPatient(id);
  } catch (e) { toast(e.message, 'error'); }
}

/* ================= Статистика ================= */
async function loadStats() {
  try {
    const s = await api('/statistics/doctor/' + me.profileId);
    $('stCards').innerHTML = [
      ['Всего записей', s.total, ''], ['Завершено приёмов', s.completed, 'green'], ['Сегодня', s.today, 'orange'],
      ['Средний рейтинг', s.averageRating ? s.averageRating.toFixed(1) + ' ★' : '—', 'red']
    ].map(([l, v, c]) => `<div class="stat ${c}"><div class="num">${v}</div><div class="lbl">${l}</div></div>`).join('');

    makeChart('stDay', () => ({ type: 'line', data: { labels: s.byDay.map(d => fmtDate(d.date + 'T00:00:00').slice(0, 5)),
      datasets: [{ label: 'Приёмы', data: s.byDay.map(d => d.appointments), borderColor: '#3498DB', backgroundColor: 'rgba(52,152,219,.15)', fill: true, tension: .3 },
                 { label: 'Пациенты', data: s.byDay.map(d => d.patients), borderColor: '#27AE60', tension: .3 }] },
      options: { responsive: true, maintainAspectRatio: false, scales: { y: { beginAtZero: true, ticks: { precision: 0 } } } } }));
    const keys = Object.keys(s.byStatus);
    makeChart('stStatus', () => ({ type: 'doughnut', data: { labels: keys.map(k => (STATUS[k] || [k])[0]),
      datasets: [{ data: keys.map(k => s.byStatus[k]), backgroundColor: keys.map(k => ({ Scheduled: '#3498DB', InProgress: '#F39C12', Completed: '#27AE60', Cancelled: '#E74C3C' }[k] || '#999')) }] },
      options: { responsive: true, maintainAspectRatio: false } }));
    makeChart('stDiag', () => ({ type: 'bar', data: { labels: s.topDiagnoses.map(d => d.diagnosis.length > 40 ? d.diagnosis.slice(0, 40) + '…' : d.diagnosis),
      datasets: [{ label: 'Случаев', data: s.topDiagnoses.map(d => d.count), backgroundColor: '#3498DB' }] },
      options: { indexAxis: 'y', responsive: true, maintainAspectRatio: false, scales: { x: { beginAtZero: true, ticks: { precision: 0 } } } } }));
  } catch (e) { toast(e.message, 'error'); }
}

/* ================= Отзывы ================= */
async function loadReviews() {
  try {
    const r = await api('/reviews/doctor/' + me.profileId);
    $('revSummary').innerHTML = `<div style="text-align:center"><div class="num" style="font-size:48px;color:var(--orange);font-weight:700">${r.count ? r.average.toFixed(1) : '—'}</div>
      ${starsHtml(r.average)}<div class="muted">Отзывов: ${r.count}</div></div>`;
    $('revList').innerHTML = r.reviews.length ? r.reviews.map(x => `
      <div class="review"><div class="toolbar" style="margin:0"><b>${esc(x.patientName)}</b><span class="muted">${fmtDate(x.createdAt)}</span></div>
      ${starsHtml(x.rating)}<p style="margin:4px 0">${esc(x.comment || 'Без комментария')}</p></div>`).join('') : '<div class="empty">Отзывов пока нет</div>';
  } catch (e) { toast(e.message, 'error'); }
}

/* ================= Старт ================= */
(async () => { try { myDoc = await api('/doctors/' + me.profileId, { silent: true }); $('specPill').textContent = myDoc.specialization; } catch { /* не критично */ } })();
loadQueue(); loadUpcoming();
setInterval(() => { loadQueue(true); loadUpcoming(); }, 20000);
