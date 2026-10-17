(() => {
  const api = window.MindSprintApi;
  const auth = window.MindSprintAuth;
  const form = document.getElementById('study-plan-form');
  const daysRoot = document.getElementById('study-plan-days');
  if (!form || !daysRoot || !api || !auth) return;

  const weekdays = [[1, 'Thứ Hai'], [2, 'Thứ Ba'], [3, 'Thứ Tư'], [4, 'Thứ Năm'], [5, 'Thứ Sáu'], [6, 'Thứ Bảy'], [0, 'Chủ Nhật']];
  const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, character => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[character]));
  const accountId = () => String(auth.getCurrentUser()?.id ?? '');
  const storageKey = id => `ms-account:${id}:study-plan-draft`;
  const status = document.getElementById('study-plan-status');
  const preview = document.getElementById('study-plan-preview');
  const itemsRoot = document.getElementById('study-plan-items');
  const warningsRoot = document.getElementById('study-plan-warnings');
  const saveButton = document.getElementById('study-plan-save');
  const generateButton = document.getElementById('study-plan-generate');
  const savedItemsRoot = document.getElementById('study-plan-saved-items');
  let plan = null;
  let busy = false;
  let renderedAccount = null;

  document.getElementById('study-plan-open-work').addEventListener('click', () => {
    document.querySelector('[data-tab="kanban"]')?.click();
  });

  daysRoot.innerHTML = weekdays.map(([day, label]) => `
    <label class="study-plan-day-row">
      <input type="checkbox" data-plan-day="${day}" aria-label="Chọn ${label}">
      <span>${label}</span>
      <input type="time" data-plan-start="${day}" value="19:00" aria-label="Giờ bắt đầu ${label}" disabled>
      <input type="time" data-plan-end="${day}" value="21:00" aria-label="Giờ kết thúc ${label}" disabled>
    </label>`).join('');

  daysRoot.addEventListener('change', event => {
    const checkbox = event.target.closest('[data-plan-day]');
    if (!checkbox) return;
    const day = checkbox.dataset.planDay;
    daysRoot.querySelectorAll(`[data-plan-start="${day}"], [data-plan-end="${day}"]`).forEach(input => { input.disabled = !checkbox.checked; });
  });

  function localDateString(date = new Date()) {
    return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
  }

  function setDefaultExamDate() {
    const exam = document.getElementById('study-plan-exam-date');
    const tomorrow = new Date(); tomorrow.setDate(tomorrow.getDate() + 1);
    exam.min = localDateString(tomorrow);
    if (!exam.value) { const suggested = new Date(); suggested.setDate(suggested.getDate() + 14); exam.value = localDateString(suggested); }
  }

  function setStatus(message, type = '') {
    status.textContent = message;
    status.dataset.type = type;
  }

  function persist() {
    if (!plan?.ownerId) return;
    try { localStorage.setItem(storageKey(plan.ownerId), JSON.stringify(plan)); } catch { /* Draft saving is best effort; server idempotency still protects retries. */ }
  }

  async function refreshSavedSessions() {
    const ownerId = accountId();
    if (!ownerId) {
      savedItemsRoot.replaceChildren(Object.assign(document.createElement('li'), { textContent: 'Đăng nhập để xem lịch ôn đã lưu.' }));
      return;
    }
    try {
      const tasks = await api.getTasks();
      if (ownerId !== accountId()) return;
      const upcoming = tasks.filter(task => task.clientRequestId && task.dueDate && Number(task.status) !== 2)
        .map(task => ({ task, date: new Date(task.dueDate) }))
        .filter(entry => !Number.isNaN(entry.date.valueOf()) && entry.date >= new Date())
        .sort((a, b) => a.date - b.date)
        .slice(0, 12);
      if (!upcoming.length) {
        savedItemsRoot.replaceChildren(Object.assign(document.createElement('li'), { textContent: 'Chưa có buổi ôn AI được xác nhận.' }));
        return;
      }
      savedItemsRoot.replaceChildren(...upcoming.map(({ task, date }) => {
        const row = document.createElement('li');
        const title = document.createElement('strong'); title.textContent = task.title;
        const when = document.createElement('span');
        when.textContent = `${new Intl.DateTimeFormat('vi-VN', { weekday: 'long', day: '2-digit', month: '2-digit', year: 'numeric' }).format(date)} · ${new Intl.DateTimeFormat('vi-VN', { hour: '2-digit', minute: '2-digit' }).format(date)}${task.durationMinutes ? ` · ${task.durationMinutes} phút` : ''}`;
        row.append(title, when);
        return row;
      }));
    } catch (error) {
      if (ownerId === accountId()) savedItemsRoot.replaceChildren(Object.assign(document.createElement('li'), { textContent: 'Chưa thể tải buổi ôn đã lưu: ' + error.message }));
    }
  }

  function restoreDraft() {
    const ownerId = accountId();
    if (ownerId !== renderedAccount) {
      form.reset();
      daysRoot.querySelectorAll('[data-plan-day]').forEach(checkbox => { checkbox.checked = false; });
      daysRoot.querySelectorAll('[data-plan-start], [data-plan-end]').forEach(input => { input.disabled = true; });
      setDefaultExamDate();
      renderedAccount = ownerId;
    }
    plan = null;
    if (!ownerId) {
      preview.hidden = true;
      setStatus('Đăng nhập để tạo kế hoạch và lưu các buổi vào tài khoản của bạn.');
      void refreshSavedSessions();
      return;
    }
    try { plan = JSON.parse(localStorage.getItem(storageKey(ownerId)) || 'null'); } catch { plan = null; }
    if (!plan || plan.ownerId !== ownerId || !Array.isArray(plan.items)) {
      plan = null;
      preview.hidden = true;
      setStatus('Bản nháp sẽ chỉ được lưu vào Công việc sau khi bạn xác nhận.');
      void refreshSavedSessions();
      return;
    }
    document.getElementById('study-plan-exam-date').value = plan.examDate || '';
    document.getElementById('study-plan-subject').value = plan.subject || '';
    document.getElementById('study-plan-topic').value = plan.topic || '';
    document.getElementById('study-plan-goal').value = plan.goal || '';
    document.getElementById('study-plan-use-weak-areas').checked = plan.includeWeakAreas !== false;
    for (const slot of plan.availableSlots || []) {
      const checkbox = daysRoot.querySelector(`[data-plan-day="${slot.dayOfWeek}"]`);
      if (!checkbox) continue;
      checkbox.checked = true;
      const start = daysRoot.querySelector(`[data-plan-start="${slot.dayOfWeek}"]`);
      const end = daysRoot.querySelector(`[data-plan-end="${slot.dayOfWeek}"]`);
      start.disabled = false; end.disabled = false;
      start.value = slot.startTime; end.value = slot.endTime;
    }
    setStatus('Đã khôi phục bản nháp của tài khoản này.');
    renderPlan();
    void refreshSavedSessions();
  }

  function renderPlan() {
    if (!plan) { preview.hidden = true; return; }
    preview.hidden = false;
    document.getElementById('study-plan-summary').textContent = plan.summary || '';
    warningsRoot.replaceChildren(...(plan.warnings || []).map(message => {
      const item = document.createElement('li'); item.textContent = message; return item;
    }));
    warningsRoot.hidden = !warningsRoot.childElementCount;
    itemsRoot.replaceChildren(...plan.items.map(item => {
      const article = document.createElement('article');
      article.className = 'study-plan-item';
      article.dataset.planId = item.clientRequestId;
      article.innerHTML = `
        <input type="checkbox" data-plan-field="selected" aria-label="Chọn buổi ${escapeHtml(item.title)}" ${item.selected ? 'checked' : ''} ${item.saved || busy ? 'disabled' : ''}>
        <div class="study-plan-item-fields">
          <label>Ngày<input type="date" data-plan-field="date" value="${escapeHtml(item.date)}" ${item.saved || busy ? 'disabled' : ''}></label>
          <label>Giờ bắt đầu<input type="time" data-plan-field="startTime" value="${escapeHtml(item.startTime)}" ${item.saved || busy ? 'disabled' : ''}></label>
          <label>Thời lượng (phút)<input type="number" data-plan-field="durationMinutes" min="15" max="180" step="5" value="${escapeHtml(item.durationMinutes)}" ${item.saved || busy ? 'disabled' : ''}></label>
          <label>Tiêu đề<input class="plan-title" type="text" data-plan-field="title" maxlength="200" value="${escapeHtml(item.title)}" ${item.saved || busy ? 'disabled' : ''}></label>
          <label class="plan-description">Nội dung<textarea data-plan-field="description" maxlength="2000" rows="2" ${item.saved || busy ? 'disabled' : ''}>${escapeHtml(item.description)}</textarea></label>
          ${item.saved ? '<p class="plan-saved">Đã thêm vào Công việc.</p>' : ''}
        </div>`;
      return article;
    }));
    const remaining = plan.items.filter(item => item.selected && !item.saved).length;
    saveButton.disabled = busy || remaining === 0;
    saveButton.textContent = remaining ? `Thêm ${remaining} buổi đã chọn vào Công việc` : 'Chọn buổi cần thêm vào Công việc';
    document.getElementById('study-plan-save-status').textContent = `${plan.items.filter(item => item.saved).length} / ${plan.items.length} buổi đã lưu.`;
  }

  function syncItemFromControl(control) {
    const article = control.closest('[data-plan-id]');
    const item = plan?.items.find(value => value.clientRequestId === article?.dataset.planId);
    if (!item) return;
    const field = control.dataset.planField;
    item[field] = field === 'selected' ? control.checked : field === 'durationMinutes' ? Number(control.value) : control.value;
    persist();
    if (field === 'selected') renderPlan();
  }

  itemsRoot.addEventListener('input', event => {
    if (event.target.matches('[data-plan-field]') && event.target.dataset.planField !== 'selected') syncItemFromControl(event.target);
  });
  itemsRoot.addEventListener('change', event => {
    if (event.target.matches('[data-plan-field]')) syncItemFromControl(event.target);
  });

  function selectedAvailability() {
    return [...daysRoot.querySelectorAll('[data-plan-day]:checked')].map(checkbox => {
      const dayOfWeek = Number(checkbox.dataset.planDay);
      return {
        dayOfWeek,
        startTime: daysRoot.querySelector(`[data-plan-start="${dayOfWeek}"]`).value,
        endTime: daysRoot.querySelector(`[data-plan-end="${dayOfWeek}"]`).value
      };
    });
  }

  form.addEventListener('submit', async event => {
    event.preventDefault();
    if (busy) return;
    if (!auth.isLoggedIn()) { auth.renderAuthModal(); setStatus('Đăng nhập để dùng tính năng này.', 'error'); return; }
    if (plan?.items.some(item => !item.saved)
      && !await window.MindSprintDialogs.confirm('Tạo bản nháp mới sẽ thay bản nháp chưa lưu hiện tại. Tiếp tục?', { title: 'Thay bản nháp hiện tại', confirmText: 'Tạo bản nháp mới' })) return;
    const ownerId = accountId();
    const availableSlots = selectedAvailability();
    if (!availableSlots.length) { setStatus('Hãy chọn ít nhất một ngày và khung giờ rảnh.', 'error'); return; }
    if (availableSlots.some(slot => !slot.startTime || !slot.endTime || slot.endTime <= slot.startTime)) {
      setStatus('Kiểm tra lại giờ bắt đầu và kết thúc của các ngày đã chọn.', 'error'); return;
    }
    const examDate = document.getElementById('study-plan-exam-date').value;
    if (!examDate || examDate <= localDateString()) { setStatus('Ngày thi phải sau hôm nay.', 'error'); return; }
    busy = true; generateButton.disabled = true; setStatus('AI đang chia nội dung vào các khung giờ còn trống…');
    try {
      const result = await api.aiStudyPlan({
        examDate,
        subject: document.getElementById('study-plan-subject').value.trim(),
        topic: document.getElementById('study-plan-topic').value.trim(),
        goal: document.getElementById('study-plan-goal').value.trim(),
        includeWeakAreas: document.getElementById('study-plan-use-weak-areas').checked,
        availableSlots
      });
      if (ownerId !== accountId()) return;
      plan = {
        ownerId, examDate, availableSlots,
        subject: document.getElementById('study-plan-subject').value.trim(),
        topic: document.getElementById('study-plan-topic').value.trim(),
        goal: document.getElementById('study-plan-goal').value.trim(),
        includeWeakAreas: document.getElementById('study-plan-use-weak-areas').checked,
        summary: result.summary || '', warnings: result.warnings || [],
        items: (result.items || []).map(item => ({ ...item, clientRequestId: item.clientRequestId || crypto.randomUUID(), selected: true, saved: false }))
      };
      persist(); renderPlan();
      setStatus(plan.items.length ? 'Bản nháp đã sẵn sàng. Chọn, sửa hoặc bỏ chọn các buổi trước khi lưu.' : 'Chưa có buổi học phù hợp với thời gian rảnh đã chọn.', plan.items.length ? '' : 'error');
    } catch (error) {
      if (ownerId === accountId()) setStatus(error.message, 'error');
    } finally {
      busy = false; generateButton.disabled = false; renderPlan();
    }
  });

  function overlaps(start, end, otherStart, otherEnd) { return start < otherEnd && otherStart < end; }
  function localTaskDateTime(task) {
    const date = new Date(task.dueDate);
    return Number.isNaN(date.valueOf()) ? null : date;
  }
  function toMinutes(date) { return date.getHours() * 60 + date.getMinutes(); }
  function hasWeeklyConflict(item, schedules) {
    const date = new Date(`${item.date}T12:00:00`);
    const day = date.getDay();
    const start = Number(item.startTime.slice(0, 2)) * 60 + Number(item.startTime.slice(3, 5));
    const end = start + Number(item.durationMinutes);
    return schedules.some(slot => {
      if (Number(slot.dayOfWeek) !== day) return false;
      const time = String(slot.startTime).slice(0, 5);
      const slotStart = Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5));
      return overlaps(start, end, slotStart, slotStart + Number(slot.durationMinutes));
    });
  }
  function hasSavedPlanConflict(item, tasks) {
    const date = item.date;
    const start = Number(item.startTime.slice(0, 2)) * 60 + Number(item.startTime.slice(3, 5));
    const end = start + Number(item.durationMinutes);
    return tasks.some(task => {
      if (!task.clientRequestId || !task.durationMinutes || !task.dueDate) return false;
      const dueDate = localTaskDateTime(task);
      return dueDate && localDateString(dueDate) === date && overlaps(start, end, toMinutes(dueDate), toMinutes(dueDate) + Number(task.durationMinutes));
    });
  }
  function validateDraftItem(item, selectedItems, schedules, tasks) {
    if (!item.date || item.date >= plan.examDate || !/^\d{2}:\d{2}$/.test(item.startTime)
      || !Number.isInteger(item.durationMinutes) || item.durationMinutes < 15 || item.durationMinutes > 180
      || !item.title?.trim()) return 'Kiểm tra ngày, giờ, thời lượng và tiêu đề của từng buổi đã chọn.';
    if (new Date(`${item.date}T${item.startTime}:00`) <= new Date()) return `Buổi ${item.title} đã qua. Hãy tạo lại kế hoạch để xếp lịch từ thời điểm hiện tại.`;
    const availability = plan.availableSlots.find(slot => Number(slot.dayOfWeek) === new Date(`${item.date}T12:00:00`).getDay());
    const start = item.startTime;
    const endMinutes = Number(start.slice(0, 2)) * 60 + Number(start.slice(3, 5)) + item.durationMinutes;
    const [endHour, endMinute] = [Math.floor(endMinutes / 60), endMinutes % 60];
    const end = `${String(endHour).padStart(2, '0')}:${String(endMinute).padStart(2, '0')}`;
    if (!availability || start < availability.startTime || end > availability.endTime) return 'Một buổi đã chỉnh sửa nằm ngoài khung giờ rảnh bạn chọn.';
    if (hasWeeklyConflict(item, schedules)) return `Buổi ${item.title} bị trùng lịch học tuần. Hãy đổi giờ hoặc bỏ chọn buổi đó.`;
    if (hasSavedPlanConflict(item, tasks)) return `Buổi ${item.title} bị trùng một buổi ôn đã lưu.`;
    const startMinutes = Number(start.slice(0, 2)) * 60 + Number(start.slice(3, 5));
    if (selectedItems.some(other => other !== item && other.date === item.date && overlaps(startMinutes, endMinutes,
      Number(other.startTime.slice(0, 2)) * 60 + Number(other.startTime.slice(3, 5)),
      Number(other.startTime.slice(0, 2)) * 60 + Number(other.startTime.slice(3, 5)) + Number(other.durationMinutes))))
      return `Buổi ${item.title} bị trùng với buổi khác đang chọn.`;
    return '';
  }

  saveButton.addEventListener('click', async () => {
    if (busy || !plan) return;
    if (!auth.isLoggedIn() || accountId() !== plan.ownerId) { auth.renderAuthModal(); setStatus('Hãy đăng nhập lại đúng tài khoản của bản nháp.', 'error'); return; }
    const pending = plan.items.filter(item => item.selected && !item.saved);
    if (!pending.length) return;
    busy = true; renderPlan(); setStatus('Đang lưu từng buổi vào Công việc…');
    try {
      // Refresh server-side calendars immediately before saving, so an edit made in another tab
      // cannot silently create an overlapping session.
      const [schedules, tasks] = await Promise.all([api.studyGetSchedule(), api.getTasks()]);
      const selected = plan.items.filter(item => item.selected && !item.saved);
      for (const item of selected) {
        const issue = validateDraftItem(item, selected, schedules, tasks);
        if (issue) throw new Error(issue);
      }
      for (const item of selected) {
        if (plan.ownerId !== accountId()) return;
        const dueDate = new Date(`${item.date}T${item.startTime}:00`);
        if (Number.isNaN(dueDate.valueOf())) throw new Error(`Ngày giờ của buổi “${item.title}” không hợp lệ.`);
        const description = [`Thời lượng: ${item.durationMinutes} phút`, item.description?.trim()].filter(Boolean).join('\n\n');
        const saved = await api.createTask({
          title: item.title.trim(), description, priority: 'Medium', status: 0,
          dueDate: dueDate.toISOString(), clientRequestId: item.clientRequestId,
          durationMinutes: item.durationMinutes
        });
        if (plan.ownerId !== accountId()) return;
        const savedDate = saved.dueDate ? new Date(saved.dueDate) : null;
        if (saved.clientRequestId && (saved.title !== item.title.trim()
          || !savedDate || savedDate.valueOf() !== dueDate.valueOf()
          || Number(saved.durationMinutes) !== item.durationMinutes || saved.description !== description)) {
          await api.updateTask(saved.id, { title: item.title.trim(), description, dueDate: dueDate.toISOString(), durationMinutes: item.durationMinutes });
          if (plan.ownerId !== accountId()) return;
        }
        item.saved = true;
        item.taskId = saved.id;
        persist(); renderPlan();
      }
      setStatus('Đã lưu các buổi đã chọn. Mở tab Công việc để xem lịch ôn.', '');
    } catch (error) {
      setStatus(`${error.message} Các buổi đã lưu được giữ nguyên; thử lại sẽ không tạo bản sao.`, 'error');
    } finally {
      busy = false; renderPlan();
      void refreshSavedSessions();
    }
  });

  document.addEventListener('DOMContentLoaded', () => {
    setDefaultExamDate();
    auth.onAuthChange(restoreDraft);
    restoreDraft();
  });
})();
