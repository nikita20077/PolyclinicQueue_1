/* ====== Общие функции для всех страниц + логика страницы входа ====== */
const CLINIC = { name: 'Поликлиника MotoSalon', address: 'г. Москва, ул. Примерная, д. 1', phone: '+7 (000) 000-00-00' }; // совпадает с appsettings.json
const API = '/api';
const STATUS = {
  Scheduled: ['Ожидает', 'badge-blue', 'fa-clock'],
  InProgress: ['На приёме', 'badge-orange', 'fa-user-doctor'],
  Completed: ['Завершён', 'badge-green', 'fa-circle-check'],
  Cancelled: ['Отменён', 'badge-red', 'fa-ban']
};

const Auth = {
  get token() { return localStorage.getItem('token'); },
  get user() { try { return JSON.parse(localStorage.getItem('user') || 'null'); } catch { return null; } },
  save(d) { localStorage.setItem('token', d.token); localStorage.setItem('user', JSON.stringify(d)); },
  clear() { localStorage.removeItem('token'); localStorage.removeItem('user'); },
  logout() { Auth.clear(); location.href = '/index.html'; },
  require(role) {
    const u = Auth.user;
    if (!Auth.token || !u || u.role !== role) { Auth.clear(); location.href = '/index.html'; return null; }
    return u;
  }
};

/* ---------- спиннер загрузки ---------- */
let loaderCount = 0;
function loader(on) {
  let el = document.getElementById('loader');
  if (!el) { el = document.createElement('div'); el.id = 'loader'; el.className = 'loader hidden'; el.innerHTML = '<div class="spinner"></div>'; document.body.appendChild(el); }
  loaderCount = Math.max(0, loaderCount + (on ? 1 : -1));
  el.classList.toggle('hidden', loaderCount === 0);
}

/* ---------- запросы к API ---------- */
async function api(path, { method = 'GET', body, allow404 = false, silent = false } = {}) {
  const headers = {};
  if (Auth.token) headers['Authorization'] = 'Bearer ' + Auth.token;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (!silent) loader(true);
  try {
    let res;
    try { res = await fetch(API + path, { method, headers, body: body !== undefined ? JSON.stringify(body) : undefined }); }
    catch { throw new Error('Ошибка подключения к серверу'); }
    if (res.status === 401 && !path.startsWith('/auth/')) { Auth.logout(); throw new Error('Сессия истекла'); }
    if (res.status === 404 && allow404) return null;
    let data = null;
    const text = await res.text();
    if (text) { try { data = JSON.parse(text); } catch { data = text; } }
    if (!res.ok) {
      let msg = data && (data.message || data.title);
      if (data && data.errors) msg = Object.values(data.errors).flat().join('; ');
      if (res.status === 403) msg = 'Недостаточно прав';
      throw new Error(msg || 'Ошибка ' + res.status);
    }
    return data;
  } finally { if (!silent) loader(false); }
}

/* ---------- утилиты ---------- */
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const pad = n => String(n).padStart(2, '0');
const fmtDate = s => s ? new Date(s).toLocaleDateString('ru-RU') : '—';
const fmtTime = s => s ? new Date(s).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' }) : '';
const fmtDateTime = s => s ? fmtDate(s) + ' ' + fmtTime(s) : '—';
const todayStr = () => { const d = new Date(); return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`; };
const isToday = s => new Date(s).toDateString() === new Date().toDateString();
const val = id => document.getElementById(id).value.trim();
const nullIfEmpty = v => v === '' || v == null ? null : v;
const ageOf = s => s ? Math.floor((Date.now() - new Date(s)) / 31557600000) : null;
const statusBadge = s => { const [t, c, i] = STATUS[s] || [s, 'badge-gray', 'fa-circle']; return `<span class="badge ${c}"><i class="fa-solid ${i}"></i>${esc(t)}</span>`; };
const starsHtml = (n, count) => {
  const r = Math.round(n || 0);
  return `<span class="stars">${'★'.repeat(r)}<span class="off">${'★'.repeat(5 - r)}</span></span>` +
    (count !== undefined ? ` <span class="muted">${n ? Number(n).toFixed(1) : '—'} (${count})</span>` : '');
};
const infoRows = pairs => pairs.map(([k, v]) => `<div class="info-row"><b>${esc(k)}</b><span>${esc(v || '—')}</span></div>`).join('');
const isNone = t => !t || /^\s*(нет|нету|не\s+(имею|принимаю|страдаю)|ничего|-)\s*\.?\s*$/i.test(t);

/* ---------- toast-уведомления (справа вверху) ---------- */
const TOAST_ICONS = { success: 'fa-circle-check', warning: 'fa-triangle-exclamation', error: 'fa-circle-xmark', info: 'fa-bell' };
function toast(msg, type = 'success') {
  if (type === 'ok') type = 'success';
  let box = document.getElementById('toasts');
  if (!box) { box = document.createElement('div'); box.id = 'toasts'; document.body.appendChild(box); }
  const t = document.createElement('div');
  t.className = 'toast ' + type;
  t.innerHTML = `<i class="fa-solid ${TOAST_ICONS[type] || TOAST_ICONS.info}"></i><span>${esc(msg)}</span>`;
  box.appendChild(t);
  setTimeout(() => { t.classList.add('out'); setTimeout(() => t.remove(), 300); }, 4200);
}

/* ---------- браузерные push-уведомления (Notification API) ---------- */
function requestNotifications() {
  if ('Notification' in window && Notification.permission === 'default')
    document.addEventListener('click', () => Notification.requestPermission(), { once: true });
}
function notify(title, body) {
  toast(body ? `${title}. ${body}` : title, 'info');
  if ('Notification' in window && Notification.permission === 'granted') {
    try { new Notification(title, { body: body || '' }); } catch { /* ignore */ }
  }
}

/* ---------- модальные окна ---------- */
const openModal = id => document.getElementById(id).classList.add('open');
const closeModal = id => document.getElementById(id).classList.remove('open');
document.addEventListener('keydown', e => { if (e.key === 'Escape') document.querySelectorAll('.modal.open').forEach(m => m.classList.remove('open')); });

/* ---------- тема (светлая по умолчанию) ---------- */
function applyTheme(t) {
  document.documentElement.dataset.theme = t;
  localStorage.setItem('theme', t);
  const b = document.getElementById('themeBtn');
  if (b) { b.textContent = t === 'dark' ? '🌙' : '🌞'; b.title = t === 'dark' ? 'Включить светлую тему' : 'Включить тёмную тему'; }
  document.dispatchEvent(new Event('themechange'));
}
function initTheme() {
  const b = document.createElement('button');
  b.id = 'themeBtn'; b.className = 'theme-btn';
  b.onclick = () => applyTheme(document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark');
  const host = document.querySelector('.topbar .user');
  if (host) host.appendChild(b); else { b.classList.add('floating'); document.body.appendChild(b); }
  applyTheme(localStorage.getItem('theme') || 'light');
}

/* ---------- вкладки + хлебные крошки ---------- */
function initTabs(onChange) {
  const btns = document.querySelectorAll('.tab-btn');
  const crumbs = document.getElementById('breadcrumbs');
  const setCrumbs = b => {
    if (!crumbs) return;
    crumbs.innerHTML = `<a id="crumbHome"><i class="fa-solid fa-house"></i>Главная</a><span>/</span><span>${esc(document.body.dataset.crumb || '')}</span><span>/</span><b>${esc(b.textContent.trim())}</b>`;
    document.getElementById('crumbHome').onclick = () => btns[0].click();
  };
  btns.forEach(b => b.addEventListener('click', () => {
    btns.forEach(x => x.classList.toggle('active', x === b));
    document.querySelectorAll('.tab').forEach(t => t.classList.toggle('active', t.id === 'tab-' + b.dataset.tab));
    setCrumbs(b);
    if (onChange) onChange(b.dataset.tab);
  }));
  if (btns.length) setCrumbs(btns[0]);
}

/* ---------- сортировка таблиц по клику на заголовок ---------- */
function makeSortable(table) {
  const ths = [...table.tHead.rows[0].cells];
  ths.forEach((th, i) => {
    if (th.classList.contains('nosort') || !th.textContent.trim()) return;
    th.classList.add('sortable');
    th.addEventListener('click', () => {
      const dir = th.dataset.dir === 'asc' ? 'desc' : 'asc';
      ths.forEach(x => { delete x.dataset.dir; x.classList.remove('asc', 'desc'); });
      th.dataset.dir = dir; th.classList.add(dir);
      const rows = [...table.tBodies[0].rows];
      if (rows.length < 2 || rows[0].cells.length <= i) return;
      const key = r => {
        const c = r.cells[i], t = (c.dataset.v ?? c.textContent).trim();
        const m = t.match(/^(\d{2})\.(\d{2})\.(\d{4})(?:\D+(\d{2}):(\d{2}))?/);
        if (m) return +new Date(m[3], m[2] - 1, m[1], m[4] || 0, m[5] || 0);
        const n = parseFloat(t.replace(',', '.'));
        return !isNaN(n) && /^-?\d/.test(t) ? n : t.toLowerCase();
      };
      rows.sort((a, b) => {
        const x = key(a), y = key(b);
        const r = typeof x === 'number' && typeof y === 'number' ? x - y : String(x).localeCompare(String(y), 'ru');
        return dir === 'asc' ? r : -r;
      });
      rows.forEach(r => table.tBodies[0].appendChild(r));
    });
  });
}

/* ---------- валидация форм: подсветка пустых обязательных полей ---------- */
function validate(ids) {
  let ok = true;
  ids.forEach(id => {
    const el = document.getElementById(id);
    const bad = !String(el.value).trim();
    el.classList.toggle('invalid', bad);
    el.addEventListener('input', () => el.classList.remove('invalid'), { once: true });
    if (bad) ok = false;
  });
  if (!ok) toast('Заполните все обязательные поля', 'warning');
  return ok;
}

/* ---------- голосовой ввод (Web Speech API, ru-RU) ---------- */
function attachVoice(btn, input) {
  const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
  if (!SR) { btn.disabled = true; btn.title = 'Голосовой ввод работает в Chrome и Edge'; return; }
  let rec = null, active = false;
  btn.addEventListener('click', () => {
    if (active) { rec.stop(); return; }
    rec = new SR(); rec.lang = 'ru-RU'; rec.interimResults = true; rec.continuous = false;
    const base = input.value ? input.value.trim() + ' ' : '';
    rec.onresult = e => { let t = ''; for (const r of e.results) t += r[0].transcript; input.value = base + t; input.dispatchEvent(new Event('input')); };
    rec.onstart = () => { active = true; btn.classList.add('recording'); };
    rec.onend = () => { active = false; btn.classList.remove('recording'); };
    rec.onerror = e => toast(e.error === 'not-allowed' ? 'Разрешите доступ к микрофону' : 'Ошибка распознавания речи', 'error');
    rec.start();
  });
}

/* ---------- красные флаги анкеты ---------- */
const FLAG_RULES = [
  { re: /боль в груди|давит в груди|давит грудь|болит грудь|сдавлива\w* в груди/i, level: 'red', icon: 'fa-heart-pulse', title: 'СРОЧНО К КАРДИОЛОГУ', note: 'Боль/давление в груди' },
  { re: /одышк|тяжело дышать|трудно дышать|не хватает воздуха|задыха/i, level: 'orange', icon: 'fa-lungs', title: 'Проверить дыхание', note: 'Одышка / затруднённое дыхание' },
  { re: /кровотеч|кровь|крови|кровит|кровян/i, level: 'red', icon: 'fa-droplet', title: 'СРОЧНО!', note: 'Кровь / кровотечение' }
];
function analyzeFlags(text) { return FLAG_RULES.filter(r => r.re.test(text || '')); }
const flagsHtml = flags => flags.map(f => `<div class="banner ${f.level}"><i class="fa-solid ${f.icon}"></i><div>${f.level === 'red' ? '🚨' : '⚠️'} ${esc(f.title)}<small>${esc(f.note)}</small></div></div>`).join('');

/* ---------- графики (Chart.js) с поддержкой темы ---------- */
const chartRegistry = {};
function makeChart(id, build) {
  const el = document.getElementById(id);
  if (!el || !window.Chart) return;
  chartRegistry[id]?.chart?.destroy();
  const cs = getComputedStyle(document.documentElement);
  Chart.defaults.color = cs.getPropertyValue('--text').trim();
  Chart.defaults.borderColor = cs.getPropertyValue('--border').trim();
  Chart.defaults.font.family = "'Segoe UI', Inter, sans-serif";
  chartRegistry[id] = { build, chart: new Chart(el, build()) };
}
document.addEventListener('themechange', () => Object.entries(chartRegistry).forEach(([id, r]) => makeChart(id, r.build)));

/* ---------- календарь выбора даты ---------- */
function renderCalendar(el, selected, onSelect, maxDays = 60) {
  const today = new Date(); today.setHours(0, 0, 0, 0);
  const max = new Date(today); max.setDate(max.getDate() + maxDays);
  const view = selected ? new Date(selected + 'T00:00:00') : new Date(today); view.setDate(1);
  const MONTHS = ['Январь', 'Февраль', 'Март', 'Апрель', 'Май', 'Июнь', 'Июль', 'Август', 'Сентябрь', 'Октябрь', 'Ноябрь', 'Декабрь'];
  function draw() {
    const y = view.getFullYear(), m = view.getMonth();
    const offset = (new Date(y, m, 1).getDay() + 6) % 7, days = new Date(y, m + 1, 0).getDate();
    let h = `<div class="cal-head"><button type="button" class="btn-icon" data-nav="-1"><i class="fa-solid fa-chevron-left"></i></button><b>${MONTHS[m]} ${y}</b><button type="button" class="btn-icon" data-nav="1"><i class="fa-solid fa-chevron-right"></i></button></div><div class="cal-grid">`;
    h += ['Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб', 'Вс'].map(d => `<div class="cal-dow">${d}</div>`).join('');
    for (let i = 0; i < offset; i++) h += '<div></div>';
    for (let d = 1; d <= days; d++) {
      const dt = new Date(y, m, d), s = `${y}-${pad(m + 1)}-${pad(d)}`;
      h += `<button type="button" class="cal-day ${s === selected ? 'selected' : ''} ${dt.getTime() === today.getTime() ? 'today' : ''}" data-d="${s}" ${dt < today || dt > max ? 'disabled' : ''}>${d}</button>`;
    }
    el.innerHTML = h + '</div>';
  }
  el.onclick = e => {
    const nav = e.target.closest('[data-nav]');
    if (nav) { view.setMonth(view.getMonth() + +nav.dataset.nav); draw(); return; }
    const d = e.target.closest('.cal-day');
    if (d && !d.disabled) { selected = d.dataset.d; draw(); onSelect(selected); }
  };
  draw();
}

/* ---------- «Подробности приёма» (клик по любой записи) ---------- */
function ensureDetailsModal() {
  if (document.getElementById('detailsModal')) return;
  const m = document.createElement('div');
  m.className = 'modal'; m.id = 'detailsModal';
  m.innerHTML = '<div class="modal-box wide"><div class="toolbar"><h2><i class="fa-solid fa-file-medical"></i>Подробности приёма</h2><button class="btn-icon" onclick="closeModal(\'detailsModal\')"><i class="fa-solid fa-xmark"></i></button></div><div id="detailsBody"></div></div>';
  m.addEventListener('click', e => { if (e.target === m) closeModal('detailsModal'); });
  document.body.appendChild(m);
}
async function showAppointmentDetails(id) {
  ensureDetailsModal(); openModal('detailsModal');
  const body = document.getElementById('detailsBody');
  body.innerHTML = '<div class="empty"><div class="spinner small"></div></div>';
  try {
    const a = await api('/appointments/' + id);
    const [q, recs] = await Promise.all([api('/questionnaires/' + id, { allow404: true }), api('/medicalrecords/patient/' + a.patientId)]);
    const rec = recs.find(r => r.appointmentId === id);
    const flags = q ? analyzeFlags(q.complaints) : [];
    body.innerHTML = `
      <div class="card" style="box-shadow:none"><h3>Запись №${a.queueNumber} ${statusBadge(a.status)}</h3>
        ${infoRows([['Дата и время', fmtDateTime(a.appointmentDate)], ['Пациент', a.patientName], ['Врач', `${a.doctorName} (${a.specialization || ''})`], ['Кабинет', a.cabinetNumber]])}</div>
      <div class="card" style="box-shadow:none"><h3><i class="fa-solid fa-clipboard-list"></i>Анкета</h3>
        ${q ? flagsHtml(flags) + infoRows([['Жалобы', q.complaints], ['Хронические', q.chronicDiseases], ['Аллергии', q.allergies], ['Лекарства', q.medications]]) : '<div class="muted">Анкета не заполнена</div>'}</div>
      <div class="card" style="box-shadow:none"><h3><i class="fa-solid fa-notes-medical"></i>Результат приёма</h3>
        ${rec ? infoRows([['Диагноз', rec.diagnosis], ['Назначения', rec.prescriptions], ['Рекомендации', rec.recommendations]]) : '<div class="muted">Запись врача пока отсутствует</div>'}</div>`;
  } catch (e) { body.innerHTML = `<div class="alert alert-error">${esc(e.message)}</div>`; }
}

/* ---------- страница входа ---------- */
(function initLoginPage() {
  const loginForm = document.getElementById('loginForm');
  initTheme();
  if (!loginForm) return;
  const regForm = document.getElementById('registerForm');
  const errBox = document.getElementById('authError');
  const showErr = m => { errBox.textContent = m; errBox.classList.remove('hidden'); };

  if (Auth.token && Auth.user) location.href = Auth.user.redirect || '/index.html';

  document.querySelectorAll('.auth-tabs button').forEach(b => b.addEventListener('click', () => {
    document.querySelectorAll('.auth-tabs button').forEach(x => x.classList.toggle('active', x === b));
    loginForm.classList.toggle('hidden', b.dataset.mode !== 'login');
    regForm.classList.toggle('hidden', b.dataset.mode !== 'register');
    errBox.classList.add('hidden');
  }));

  async function submit(path, body) {
    errBox.classList.add('hidden');
    try { const d = await api(path, { method: 'POST', body }); Auth.save(d); toast('Добро пожаловать!'); location.href = d.redirect; }
    catch (e) { showErr(e.message); toast(e.message, 'error'); }
  }

  loginForm.addEventListener('submit', e => {
    e.preventDefault();
    if (!validate(['loginName', 'loginPass'])) return;
    submit('/auth/login', { login: val('loginName'), password: document.getElementById('loginPass').value });
  });
  regForm.addEventListener('submit', e => {
    e.preventDefault();
    if (!validate(['regName', 'regBirth', 'regPhone', 'regLogin', 'regPass', 'regPass2'])) return;
    const email = val('regEmail');
    if (email && !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)) { document.getElementById('regEmail').classList.add('invalid'); toast('Проверьте email', 'warning'); return; }
    if (document.getElementById('regPass').value !== document.getElementById('regPass2').value) {
      document.getElementById('regPass2').classList.add('invalid'); toast('Пароли не совпадают', 'warning'); return;
    }
    submit('/auth/register', {
      login: val('regLogin'), password: document.getElementById('regPass').value, fullName: val('regName'),
      birthDate: nullIfEmpty(val('regBirth')), gender: nullIfEmpty(val('regGender')), phoneNumber: nullIfEmpty(val('regPhone')),
      policyNumber: nullIfEmpty(val('regPolicy')), email: nullIfEmpty(email), address: nullIfEmpty(val('regAddress'))
    });
  });
})();

/* На страницах кабинетов тему инициализирует сам скрипт страницы после появления .topbar */
if (document.querySelector('.topbar') && !document.getElementById('themeBtn')) initTheme();
