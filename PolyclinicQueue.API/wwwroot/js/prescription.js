/* PDF-рецепт через jsPDF. Текст рисуется на canvas (так корректно отображается кириллица),
   затем страница вставляется в PDF. Скачивается автоматически. */
function wrapText(ctx, text, maxW) {
  const out = [];
  String(text || '').split('\n').forEach(par => {
    let line = '';
    par.split(/\s+/).forEach(w => {
      const test = line ? line + ' ' + w : w;
      if (ctx.measureText(test).width > maxW && line) { out.push(line); line = w; } else line = test;
    });
    out.push(line);
  });
  return out;
}

function downloadPrescription(d) {
  if (!window.jspdf) { toast('Библиотека jsPDF не загрузилась (проверьте интернет)', 'error'); return; }
  const W = 1588, H = 2246, L = 130, R = W - 130;
  const c = document.createElement('canvas'); c.width = W; c.height = H;
  const x = c.getContext('2d');
  x.fillStyle = '#fff'; x.fillRect(0, 0, W, H);
  const F = '"Segoe UI", Arial, sans-serif';
  let y = 150;

  const text = (t, font, color, xx = L) => { x.font = font; x.fillStyle = color; x.fillText(t, xx, y); };
  const para = (t, font = `34px ${F}`, color = '#2c3e50', gap = 46) => {
    x.font = font; x.fillStyle = color;
    wrapText(x, t, R - L).forEach(l => { x.fillText(l, L, y); y += gap; });
  };

  // шапка
  text(CLINIC.name, `bold 62px ${F}`, '#3498DB'); y += 56;
  text(CLINIC.address, `30px ${F}`, '#666'); y += 42;
  text('Тел.: ' + CLINIC.phone, `30px ${F}`, '#666'); y += 26;
  x.fillStyle = '#3498DB'; x.fillRect(L, y, R - L, 5); y += 90;

  x.textAlign = 'center'; text('РЕЦЕПТ', `bold 64px ${F}`, '#2c3e50', W / 2); x.textAlign = 'left'; y += 90;

  const field = (k, v) => { x.font = `bold 34px ${F}`; x.fillStyle = '#2c3e50'; x.fillText(k, L, y); const w = x.measureText(k + ' ').width;
    x.font = `34px ${F}`; wrapText(x, v || '—', R - L - w).forEach((l, i) => { x.fillText(l, L + w, y); y += 46; }); y += 6; };
  field('Пациент:', d.patientName);
  field('Дата рождения:', d.birthDate ? fmtDate(d.birthDate) : '—');
  field('Дата приёма:', fmtDate(d.date || new Date()));
  field('Диагноз:', d.diagnosis);
  y += 20;

  text('Назначения:', `bold 36px ${F}`, '#3498DB'); y += 52;
  const drugs = String(d.prescriptions || '').split('\n').map(s => s.trim()).filter(Boolean);
  if (!drugs.length) para('—');
  drugs.forEach((l, i) => para(`${i + 1}. ${l}`));
  y += 24;

  if (d.recommendations) { text('Рекомендации:', `bold 36px ${F}`, '#3498DB'); y += 52; para(d.recommendations); }

  // подпись
  y = Math.max(y + 100, H - 330);
  x.font = `34px ${F}`; x.fillStyle = '#2c3e50';
  x.fillText('Врач: ' + d.doctorName + (d.specialization ? ` (${d.specialization})` : ''), L, y); y += 90;
  x.fillText('Подпись: ____________________', L, y);
  x.fillText('Дата: ' + fmtDate(d.date || new Date()), R - 360, y);
  x.font = `24px ${F}`; x.fillStyle = '#999';
  x.fillText('Электронный документ · ' + CLINIC.name, L, H - 90);

  const pdf = new window.jspdf.jsPDF({ unit: 'mm', format: 'a4' });
  pdf.addImage(c.toDataURL('image/jpeg', 0.95), 'JPEG', 0, 0, 210, 297);
  pdf.save(`Recept_${todayStr()}.pdf`);
  toast('Рецепт сформирован и скачан');
}
