const me = Auth.require('Patient');
if (me) document.getElementById('userName').textContent = me.fullName;

const $ = id => document.getElementById(id);
const ACTIVE = ['Scheduled', 'InProgress'];
let appts = [], doctors = [], selDoctor = null, selDate = todayStr(), selTime = null;
let patientInfo = null, timelineItems = [], reviewAppt = null, reviewRating = 0, currentQ = null;
const prevStatus = {};

requestNotifications();
makeSortable($('myApptsTable'));

function upcoming() {
  const start = new Date(); start.setHours(0, 0, 0, 0);
  return appts.filter(a => ACTIVE.includes(a.status) && new Date(a.appointmentDate) >= start)
              .sort((a, b) => new Date(a.appointmentDate) - new Date(b.appointmentDate));
}

initTabs(tab => {
  if (tab === 'book') loadSlots();
  if (tab === 'questionnaire') fillQuestionnaireSelect();
  if (tab === 'queue') renderQueue();
  if (tab === 'history') loadHistory();
});

/* ================= Данные ================= */
async function loadAppts(silent = false) {
  try {
    appts = await api('/appointments/patient/' + me.profileId, { silent });
    renderHome(); renderQueue();
    if ($('tab-history').classList.contains('active')) loadHistory(true);
  } catch (e) { toast(e.message, 'error'); }
}

/* ================= Главная ================= */
function renderHome() {
  const h = new Date().getHours();
  $('greeting').innerHTML = `<i class="fa-solid fa-hand-holding-heart"></i>${h < 6 ? 'Доброй ночи' : h < 12 ? 'Доброе утро' : h < 18 ? 'Добрый день' : 'Добрый вечер'}, ${esc(me.fullName)}!`;
  const n = upcoming()[0];
  $('nextAppt').innerHTML = n
    ? `<p class="muted">Ближайшая запись:</p>
       <p style="font-size:17px"><b>${fmtDateTime(n.appointmentDate)}</b> — ${esc(n.doctorName)} (${esc(n.specialization)}), кабинет ${esc(n.cabinetNumber || '—')} ${statusBadge(n.status)}</p>`
    : `<p class="muted">У вас нет предстоящих записей.</p><button class="btn" onclick="document.querySelector('[data-tab=book]').click()"><i class="fa-solid fa-calendar-plus"></i>Записаться к врачу</button>`;

  $('homeStats').innerHTML = [
    ['Всего записей', appts.length, ''], ['Ожидаются', upcoming().length, 'green'],
    ['Завершённых приёмов', appts.filter(a => a.status === 'Completed').length, 'orange']
  ].map(([l, v, c]) => `<div class="stat ${c}"><div class="num">${v}</div><div class="lbl">${l}</div></div>`).join('');

  $('myAppts').innerHTML = appts.length ? appts.map(a => `
    <tr class="clickable" data-id="${a.id}"><td>${fmtDateTime(a.appointmentDate)}</td>
    <td>${esc(a.doctorName)}<br><span class="muted">${esc(a.specialization || '')}</span></td>
    <td>${esc(a.cabinetNumber || '—')}</td><td>${a.queueNumber}</td><td>${statusBadge(a.status)}</td>
    <td>${a.status === 'Scheduled' ? `<button class="btn btn-light btn-sm" data-cancel="${a.id}"><i class="fa-solid fa-xmark"></i>Отменить</button>` : ''}</td></tr>`).join('')
    : '<tr><td colspan="6" class="empty">Записей пока нет</td></tr>';
}
document.addEventListener('click', e => {
  const cn = e.target.closest('[data-cancel]'); if (cn) { cancelAppt(+cn.dataset.cancel); return; }
  const row = e.target.closest('#myAppts tr.clickable, #upcomingList .tl-card'); if (row) showAppointmentDetails(+row.dataset.id);
});

async function cancelAppt(id) {
  if (!confirm('Отменить запись?')) return;
  try { await api(`/appointments/${id}/status`, { method: 'PUT', body: { status: 'Cancelled' } }); toast('Запись отменена'); loadAppts(true); }
  catch (e) { toast(e.message, 'error'); }
}

/* ================= Запись к врачу ================= */
async function initBooking() {
  try {
    doctors = await api('/doctors');
    const specs = [...new Set(doctors.map(d => d.specialization))].sort((a, b) => a.localeCompare(b, 'ru'));
    $('specFilter').innerHTML = '<option value="">Все специальности</option>' + specs.map(s => `<option>${esc(s)}</option>`).join('');
    renderDoctors();
    renderCalendar($('bCal'), selDate, d => { selDate = d; loadSlots(); });
    loadAppts(); loadSlots();
  } catch (e) { toast(e.message, 'error'); }
}
function renderDoctors() {
  const spec = $('specFilter').value, list = doctors.filter(d => !spec || d.specialization === spec);
  if (!list.some(d => d.id === selDoctor)) selDoctor = list.length ? list[0].id : null;
  $('doctorPick').innerHTML = list.length ? list.map(d => `
    <div class="doc-card ${d.id === selDoctor ? 'selected' : ''}" data-id="${d.id}"><div class="avatar">${esc(d.fullName.split(' ').slice(0, 2).map(x => x[0]).join(''))}</div>
    <b>${esc(d.fullName)}</b><br><span class="muted">${esc(d.specialization)} · каб. ${esc(d.cabinetNumber || '—')}</span>
    ${d.experienceYears != null ? `<br><span class="muted">Стаж ${d.experienceYears} лет${d.category ? ' · ' + esc(d.category) + ' категория' : ''}</span>` : ''}
    <br>${starsHtml(d.rating, d.reviewsCount)}</div>`).join('') : '<div class="empty">Нет доступных врачей</div>';
}
$('specFilter').addEventListener('change', () => { renderDoctors(); loadSlots(); });
$('doctorPick').addEventListener('click', e => {
  const c = e.target.closest('.doc-card'); if (!c) return;
  selDoctor = +c.dataset.id; renderDoctors(); loadSlots();
});

async function loadSlots() {
  selTime = null; $('btnBook').disabled = true;
  $('slotDate').textContent = selDate ? '· ' + fmtDate(selDate + 'T00:00:00') : '';
  if (!selDoctor || !selDate) { $('slots').innerHTML = '<div class="muted">Выберите врача и дату</div>'; return; }
  let busy = [];
  try { busy = await api(`/appointments/busy/${selDoctor}?date=${selDate}`, { silent: true }); } catch (e) { toast(e.message, 'error'); }
  const now = new Date(), html = [];
  for (let h = 9; h < 18; h++) for (const m of ['00', '30']) {
    const t = `${pad(h)}:${m}`;
    html.push(`<button class="slot" data-time="${t}" ${busy.includes(t) || new Date(`${selDate}T${t}:00`) <= now ? 'disabled' : ''}>${t}</button>`);
  }
  $('slots').innerHTML = html.join('');
}
$('slots').addEventListener('click', e => {
  const b = e.target.closest('.slot'); if (!b || b.disabled) return;
  document.querySelectorAll('#slots .slot').forEach(s => s.classList.remove('selected'));
  b.classList.add('selected'); selTime = b.dataset.time; $('btnBook').disabled = false;
});
$('btnBook').addEventListener('click', async () => {
  if (!selDoctor || !selDate || !selTime) { toast('Выберите врача, дату и время', 'warning'); return; }
  try {
    const r = await api('/appointments', { method: 'POST', body: { doctorId: selDoctor, appointmentDate: `${selDate}T${selTime}:00` } });
    toast(`Запись создана! Ваш номер в очереди: ${r.queueNumber}`);
    await loadAppts(true); loadSlots();
    document.querySelector('[data-tab=questionnaire]').click();
  } catch (e) { toast(e.message, 'error'); loadSlots(); }
});

/* ================= Анкета ================= */
function fillQuestionnaireSelect() {
  const list = upcoming();
  $('qEmpty').classList.toggle('hidden', list.length > 0);
  $('qAppt').parentElement.classList.toggle('hidden', !list.length);
  $('qForm').classList.add('hidden');
  const cur = $('qAppt').value;
  $('qAppt').innerHTML = list.map(a => `<option value="${a.id}">${fmtDateTime(a.appointmentDate)} — ${esc(a.doctorName)}</option>`).join('');
  if (list.some(a => String(a.id) === cur)) $('qAppt').value = cur;
  if (list.length) loadQuestionnaire();
}
async function loadQuestionnaire() {
  const id = $('qAppt').value; if (!id) return;
  try {
    currentQ = await api('/questionnaires/' + id, { allow404: true });
    $('qComplaints').value = currentQ?.complaints || ''; $('qChronic').value = currentQ?.chronicDiseases || '';
    $('qAllergies').value = currentQ?.allergies || ''; $('qMeds').value = currentQ?.medications || '';
    $('btnSaveQ').innerHTML = `<i class="fa-solid fa-floppy-disk"></i>${currentQ ? 'Обновить анкету' : 'Сохранить анкету'}`;
    $('qForm').classList.remove('hidden');
  } catch (e) { toast(e.message, 'error'); }
}
$('qAppt').addEventListener('change', loadQuestionnaire);
[['micC', 'qComplaints'], ['micCh', 'qChronic'], ['micA', 'qAllergies'], ['micM', 'qMeds']].forEach(([b, i]) => attachVoice($(b), $(i)));
$('btnSaveQ').addEventListener('click', async () => {
  if (!validate(['qComplaints'])) return;
  const body = { appointmentId: +$('qAppt').value, complaints: nullIfEmpty(val('qComplaints')), chronicDiseases: nullIfEmpty(val('qChronic')),
    allergies: nullIfEmpty(val('qAllergies')), medications: nullIfEmpty(val('qMeds')) };
  try {
    if (currentQ) await api('/questionnaires/' + currentQ.id, { method: 'PUT', body }); else await api('/questionnaires', { method: 'POST', body });
    toast('Анкета сохранена'); loadQuestionnaire();
  } catch (e) { toast(e.message, 'error'); }
});

/* ================= Моя очередь (живая) ================= */
async function renderQueue() {
  const list = upcoming();
  const a = list.find(x => isToday(x.appointmentDate)) || list[0];
  $('queueEmpty').classList.toggle('hidden', !!a); $('queueInfo').classList.toggle('hidden', !a);
  if (!a) return;

  let st = null;
  try { st = await api(`/appointments/${a.id}/queue-status`, { silent: true }); } catch { /* показываем данные из списка */ }
  const ahead = st ? st.peopleAhead : a.peopleAhead, est = st ? st.estimatedMinutes : ahead * 10, today = isToday(a.appointmentDate);

  let note;
  if (a.status === 'InProgress') note = '<p style="color:var(--green);font-size:18px"><b><i class="fa-solid fa-user-doctor"></i>Сейчас ваш приём!</b></p>';
  else if (!today) note = '<p class="muted">Очередь начнётся в день приёма.</p>';
  else if (ahead === 0) note = '<p style="color:var(--orange);font-size:18px"><b><i class="fa-solid fa-bell"></i>Вы следующий — подойдите к кабинету</b></p>';
  else note = `<p>Перед вами: <b>${ahead}</b> · примерно <b>${est} мин</b></p>`;

  $('queueInfo').innerHTML = `<div class="queue-big">
    <div class="muted">Ваш номер в очереди</div><div class="number">${a.queueNumber}</div>
    <p>${statusBadge(a.status)}</p>${note}
    ${st && st.currentNumber ? `<p class="muted">Сейчас на приёме: №${st.currentNumber}</p>` : ''}
    <p>${esc(a.doctorName)} (${esc(a.specialization || '')})<br>Кабинет: <b>${esc(a.cabinetNumber || '—')}</b> · ${fmtDateTime(a.appointmentDate)}</p>
    <p class="muted" style="font-size:13px"><i class="fa-solid fa-rotate"></i>Обновляется автоматически</p></div>`;
  checkReminders(a, st);
}
function checkReminders(a, st) {
  if (prevStatus[a.id] === 'Scheduled' && a.status === 'InProgress') notify('Ваш приём начался', `Кабинет ${a.cabinetNumber || ''}`);
  prevStatus[a.id] = a.status;
  if (a.status !== 'Scheduled' || !isToday(a.appointmentDate)) return;
  const remain = Math.max(st ? st.estimatedMinutes : a.peopleAhead * 10, (new Date(a.appointmentDate) - Date.now()) / 60000);
  const key = 'remind_' + a.id;
  if (remain <= 5 && !localStorage.getItem(key)) {
    localStorage.setItem(key, '1');
    notify('Ваша очередь через 5 минут', `Подойдите к кабинету ${a.cabinetNumber || ''}`);
  }
}

/* ================= История записей: «Ожидаются» + история обращений ================= */
async function loadHistory(silent = false) {
  try {
    [timelineItems, patientInfo] = await Promise.all([
      api(`/patients/${me.profileId}/timeline`, { silent }), patientInfo || api('/patients/' + me.profileId, { silent })]);
    const waiting = timelineItems.filter(t => ACTIVE.includes(t.status)).sort((a, b) => new Date(a.appointmentDate) - new Date(b.appointmentDate));
    const past = timelineItems.filter(t => !ACTIVE.includes(t.status));

    $('upcomingList').innerHTML = waiting.length ? waiting.map(t => `
      <div class="tl-card" data-id="${t.appointmentId}" style="margin-bottom:10px">
        <div class="tl-head"><b><i class="fa-regular fa-calendar"></i>${fmtDateTime(t.appointmentDate)}</b>${statusBadge(t.status)}</div>
        <div class="muted">${esc(t.doctorName)} · ${esc(t.specialization || '')} · очередь №${t.queueNumber}</div>
        ${t.status === 'Scheduled' ? `<div class="tl-actions"><button class="btn btn-light btn-sm" data-cancel="${t.appointmentId}"><i class="fa-solid fa-xmark"></i>Отменить</button></div>` : ''}
      </div>`).join('') : '<div class="empty">Предстоящих записей нет</div>';

    const color = { Completed: 'green', Cancelled: 'red' };
    $('timeline').innerHTML = past.length ? past.map(t => `
      <div class="tl-item"><div class="tl-dot ${color[t.status] || ''}"></div>
      <div class="tl-card" data-id="${t.appointmentId}">
        <div class="tl-head"><b>${fmtDateTime(t.appointmentDate)}</b>${statusBadge(t.status)}</div>
        <div class="muted">${esc(t.doctorName)} · ${esc(t.specialization || '')}</div>
        ${t.diagnosis ? `<p><b>Диагноз:</b> ${esc(t.diagnosis)}</p><p><b>Назначения:</b> ${esc(t.prescriptions || '—')}</p><p><b>Рекомендации:</b> ${esc(t.recommendations || '—')}</p>` : ''}
        <div class="tl-actions">
          ${t.recordId ? `<button class="btn btn-light btn-sm" data-pdf="${t.appointmentId}"><i class="fa-solid fa-file-pdf"></i>Скачать рецепт PDF</button>` : ''}
          ${t.status === 'Completed' && t.rating == null ? `<button class="btn btn-warning btn-sm" data-rate="${t.appointmentId}"><i class="fa-solid fa-star"></i>Оценить врача</button>` : ''}
          ${t.rating != null ? `<span>Ваша оценка: ${starsHtml(t.rating)}</span>` : ''}
        </div></div></div>`).join('') : '<div class="empty">История обращений пока пуста</div>';
  } catch (e) { toast(e.message, 'error'); }
}
$('timeline').addEventListener('click', e => {
  const pdf = e.target.closest('[data-pdf]');
  if (pdf) {
    const t = timelineItems.find(x => x.appointmentId === +pdf.dataset.pdf);
    downloadPrescription({ patientName: me.fullName, birthDate: patientInfo?.birthDate, diagnosis: t.diagnosis, prescriptions: t.prescriptions,
      recommendations: t.recommendations, doctorName: t.doctorName, specialization: t.specialization, date: t.appointmentDate });
    return;
  }
  const rate = e.target.closest('[data-rate]');
  if (rate) { openReview(+rate.dataset.rate); return; }
  const card = e.target.closest('.tl-card'); if (card) showAppointmentDetails(+card.dataset.id);
});

/* ================= Рейтинг врача ================= */
function setStars(n) { reviewRating = n; document.querySelectorAll('#starPick button').forEach(b => b.classList.toggle('on', +b.dataset.v <= n)); }
function openReview(id) {
  reviewAppt = timelineItems.find(x => x.appointmentId === id); setStars(0); $('reviewText').value = '';
  $('reviewDoctor').textContent = reviewAppt.doctorName; openModal('reviewModal');
}
$('starPick').addEventListener('click', e => { const b = e.target.closest('button'); if (b) setStars(+b.dataset.v); });
$('btnReview').addEventListener('click', async () => {
  if (!reviewRating) { toast('Поставьте оценку звёздами', 'warning'); return; }
  try {
    await api('/reviews', { method: 'POST', body: { appointmentId: reviewAppt.appointmentId, rating: reviewRating, comment: nullIfEmpty(val('reviewText')) } });
    closeModal('reviewModal'); toast('Спасибо за отзыв!'); loadHistory();
  } catch (e) { toast(e.message, 'error'); }
});

initBooking();
setInterval(() => loadAppts(true), 15000);
