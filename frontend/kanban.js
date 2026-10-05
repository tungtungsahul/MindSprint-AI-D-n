/**
 * MindSprint AI — Kanban Task Board Manager
 * Interactive 3-Column HTML5 Drag-and-Drop, Touch/Click-to-Move, and Task CRUD Synchronization
 */

(function () {
const api = window.MindSprintApi;
const auth = window.MindSprintAuth;
const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
function showToast(message, type = 'info') {
  const status = document.getElementById('kanban-status');
  status.textContent = message;
  status.dataset.type = type;
}

class KanbanBoard {
  constructor() {
    this.tasks = [];
    this.filterText = '';
    this.filterPriority = 'all';
    this.currentEditingTaskId = null;
    this.version = 0;
    this.pending = false;

    // Column DOM Elements
    this.columns = {
      Todo: document.getElementById('col-todo-list'),
      InProgress: document.getElementById('col-inprogress-list'),
      Completed: document.getElementById('col-completed-list')
    };

    // Counter Badges
    this.counters = {
      Todo: document.getElementById('todo-count'),
      InProgress: document.getElementById('inprogress-count'),
      Completed: document.getElementById('completed-count')
    };

    // Modal
    this.taskModal = document.getElementById('task-modal');
  }

  init() {
    this.bindBoardEvents();
    this.bindDragDropEvents();
    this.bindModalEvents();
    this.render();
    document.getElementById('kanban-login').addEventListener('click', () => auth.renderAuthModal());
    document.getElementById('kanban-reload').addEventListener('click', () => this.loadTasks());
    document.addEventListener('keydown', event => { if (event.key === 'Escape') this.closeTaskModal(); });
    if (!auth.isLoggedIn()) this.loadTasks();
    auth.onAuthChange(() => {
      this.version++;
      this.pending = false;
      this.tasks = [];
      this.filterText = ''; this.filterPriority = 'all';
      document.getElementById('kanban-search').value = '';
      document.getElementById('kanban-priority-filter').value = 'all';
      this.setBusy(false); this.closeTaskModal(); this.render();
      return this.loadTasks();
    });
  }

  bindBoardEvents() {
    // Search input
    const searchInput = document.getElementById('kanban-search');
    if (searchInput) {
      searchInput.addEventListener('input', (e) => {
        this.filterText = e.target.value.toLowerCase().trim();
        this.render();
      });
    }

    // Priority filter
    const prioritySelect = document.getElementById('kanban-priority-filter');
    if (prioritySelect) {
      prioritySelect.addEventListener('change', (e) => {
        this.filterPriority = e.target.value;
        this.render();
      });
    }

    // Quick Add buttons in column headers
    document.querySelectorAll('.btn-add-col-task').forEach(btn => {
      btn.addEventListener('click', (e) => {
        const status = e.currentTarget.dataset.status || 'Todo';
        this.openTaskModal(status);
      });
    });

    // Main "+ Thêm Task" button
    const btnNewTask = document.getElementById('btn-new-task');
    if (btnNewTask) {
      btnNewTask.addEventListener('click', () => this.openTaskModal('Todo'));
    }
  }

  bindDragDropEvents() {
    // Set up drag-and-drop on each column list container
    Object.entries(this.columns).forEach(([status, colElement]) => {
      if (!colElement) return;

      colElement.addEventListener('dragover', (e) => {
        e.preventDefault();
        e.dataTransfer.dropEffect = 'move';
        colElement.classList.add('column-drag-over');
      });

      colElement.addEventListener('dragleave', (e) => {
        // Only remove if leaving the column container itself
        if (!colElement.contains(e.relatedTarget)) {
          colElement.classList.remove('column-drag-over');
        }
      });

      colElement.addEventListener('drop', async (e) => {
        e.preventDefault();
        colElement.classList.remove('column-drag-over');

        const taskId = e.dataTransfer.getData('text/plain');
        if (!taskId) return;

        const targetStatus = status; // 'Todo' | 'InProgress' | 'Completed'
        await this.handleTaskMove(taskId, targetStatus);
      });
    });
  }

  bindModalEvents() {
    if (!this.taskModal) return;

    // Modal Close
    const closeBtn = document.getElementById('task-modal-close');
    const cancelBtn = document.getElementById('task-modal-cancel');

    if (closeBtn) closeBtn.addEventListener('click', () => this.closeTaskModal());
    if (cancelBtn) cancelBtn.addEventListener('click', () => this.closeTaskModal());

    this.taskModal.addEventListener('click', (e) => {
      if (e.target === this.taskModal) this.closeTaskModal();
    });

    // Form Submit
    const form = document.getElementById('task-form');
    if (form) {
      form.addEventListener('submit', (e) => {
        e.preventDefault();
        this.handleCreateTask();
      });
    }
  }

  async loadTasks() {
    const version = this.version;
    document.getElementById('kanban-login').hidden = auth.isLoggedIn();
    document.getElementById('kanban-reload').hidden = true;
    if (!auth.isLoggedIn()) {
      this.tasks = []; this.render();
      showToast('Đăng nhập bằng tài khoản chung để lưu và đồng bộ công việc.');
      return false;
    }
    try {
      const data = await api.getTasks();
      if (version !== this.version) return false;
      this.tasks = data.map(task => ({ ...task, status: this.normalizeStatus(task.status), due_date: (task.dueDate || '').slice(0, 10) }));
      this.render();
      showToast(this.tasks.length ? 'Kéo thả thẻ hoặc dùng nút mũi tên để chuyển trạng thái.' : 'Chưa có công việc. Thêm công việc đầu tiên của bạn.');
      return true;
    } catch (err) {
      if (version !== this.version) return false;
      showToast('Chưa tải được công việc: ' + err.message, 'error');
      document.getElementById('kanban-reload').hidden = false;
      return false;
    }
  }

  render() {
    // Clear all columns
    Object.values(this.columns).forEach(col => {
      if (col) col.innerHTML = '';
    });

    // Filter tasks
    const filteredTasks = this.tasks.filter(t => {
      const matchesSearch = !this.filterText ||
        (t.title && t.title.toLowerCase().includes(this.filterText)) ||
        (t.description && t.description.toLowerCase().includes(this.filterText));

      const matchesPriority = this.filterPriority === 'all' ||
        (t.priority && t.priority.toLowerCase() === this.filterPriority.toLowerCase());

      return matchesSearch && matchesPriority;
    });

    const statusCounts = { Todo: 0, InProgress: 0, Completed: 0 };

    filteredTasks.forEach(task => {
      const normStatus = this.normalizeStatus(task.status);
      statusCounts[normStatus] = (statusCounts[normStatus] || 0) + 1;

      const card = this.createCardElement(task);
      const targetCol = this.columns[normStatus] || this.columns.Todo;
      if (targetCol) {
        targetCol.appendChild(card);
      }
    });

    // Update Counter Badges
    Object.keys(this.counters).forEach(key => {
      if (this.counters[key]) {
        this.counters[key].textContent = statusCounts[key] || 0;
      }
    });

    // Render empty placeholders if column has 0 tasks
    Object.entries(this.columns).forEach(([key, col]) => {
      if (col && col.children.length === 0) {
        col.innerHTML = `
          <div class="empty-column-placeholder">
            Không có task nào trong cột này
          </div>
        `;
      }
    });
  }

  normalizeStatus(status) {
    if (status === 1 || status === 'Doing') return 'InProgress';
    if (status === 2 || status === 'Done') return 'Completed';
    if (!status) return 'Todo';
    const s = String(status).toLowerCase();
    if (s.includes('progress') || s === 'inprogress') return 'InProgress';
    if (s.includes('complete') || s === 'done') return 'Completed';
    return 'Todo';
  }

  createCardElement(task) {
    const card = document.createElement('div');
    card.className = 'kanban-card';
    card.draggable = !this.pending;
    card.dataset.id = task.id;

    const priority = ['Low', 'Medium', 'High'].includes(task.priority) ? task.priority : 'Medium';
    const priorityLabel = {Low: 'Thấp', Medium: 'Trung bình', High: 'Cao'}[priority];
    const priorityClass = `badge-${priority.toLowerCase()}`;
    const priorityIcon = priority.toLowerCase() === 'high' ? '🔥' : (priority.toLowerCase() === 'medium' ? '⚡' : '🍃');

    // Determine move buttons based on current status
    const normStatus = this.normalizeStatus(task.status);
    let moveButtonsHtml = '';
    if (normStatus === 'Todo') {
      moveButtonsHtml = `<button class="btn-move btn-move-right" title="Chuyển sang Đang làm (InProgress)">→</button>`;
    } else if (normStatus === 'InProgress') {
      moveButtonsHtml = `
        <button class="btn-move btn-move-left" title="Chuyển về Chưa làm (Todo)">←</button>
        <button class="btn-move btn-move-right" title="Chuyển sang Hoàn thành (Completed)">→</button>
      `;
    } else if (normStatus === 'Completed') {
      moveButtonsHtml = `<button class="btn-move btn-move-left" title="Chuyển lại Đang làm (InProgress)">←</button>`;
    }

    const dueDateHtml = task.due_date ? `
      <span class="card-date" title="Hạn hoàn thành">
        📅 ${escapeHtml(task.due_date)}
      </span>
    ` : '<span></span>';

    card.innerHTML = `
      <div class="card-top-bar">
        <span class="badge ${priorityClass}">${priorityIcon} ${priorityLabel}</span>
        <div class="card-header-actions">
          <button class="btn-icon btn-card-edit" title="Sửa task">✏️</button>
          <button class="btn-icon btn-card-delete" title="Xóa task">🗑️</button>
        </div>
      </div>
      <h4 class="card-title">${escapeHtml(task.title)}</h4>
      ${task.description ? `<p class="card-desc">${escapeHtml(task.description)}</p>` : ''}
      <div class="card-footer">
        ${dueDateHtml}
        <div class="card-move-actions">
          ${moveButtonsHtml}
        </div>
      </div>
    `;

    // Bind Card-level Events
    this.bindCardEvents(card, task);

    return card;
  }

  bindCardEvents(card, task) {
    // HTML5 Drag Start
    card.addEventListener('dragstart', (e) => {
      e.dataTransfer.setData('text/plain', String(task.id));
      e.dataTransfer.effectAllowed = 'move';
      card.classList.add('is-dragging');
    });

    // Drag End
    card.addEventListener('dragend', () => {
      card.classList.remove('is-dragging');
    });

    // Edit Button
    const btnEdit = card.querySelector('.btn-card-edit');
    if (btnEdit) {
      btnEdit.addEventListener('click', (e) => {
        e.stopPropagation();
        this.handleEditTask(task.id);
      });
    }

    // Delete Button
    const btnDelete = card.querySelector('.btn-card-delete');
    if (btnDelete) {
      btnDelete.addEventListener('click', (e) => {
        e.stopPropagation();
        this.handleDeleteTask(task.id);
      });
    }

    // Move Left Button
    const btnMoveLeft = card.querySelector('.btn-move-left');
    if (btnMoveLeft) {
      btnMoveLeft.addEventListener('click', (e) => {
        e.stopPropagation();
        const norm = this.normalizeStatus(task.status);
        const targetStatus = norm === 'Completed' ? 'InProgress' : 'Todo';
        this.handleTaskMove(task.id, targetStatus);
      });
    }

    // Move Right Button
    const btnMoveRight = card.querySelector('.btn-move-right');
    if (btnMoveRight) {
      btnMoveRight.addEventListener('click', (e) => {
        e.stopPropagation();
        const norm = this.normalizeStatus(task.status);
        const targetStatus = norm === 'Todo' ? 'InProgress' : 'Completed';
        this.handleTaskMove(task.id, targetStatus);
      });
    }
  }

  requireAccount() {
    if (auth.isLoggedIn()) return true;
    auth.renderAuthModal();
    return false;
  }

  setBusy(pending) {
    document.querySelectorAll('#task-modal button, #task-modal input, #task-modal select, #task-modal textarea, #tab-content-kanban .btn-add-col-task, #btn-new-task').forEach(element => { element.disabled = pending; });
    document.querySelectorAll('#tab-content-kanban .kanban-card').forEach(card => { card.draggable = !pending; });
  }

  async mutate(work, message) {
    if (this.pending || !this.requireAccount()) return;
    const version = this.version;
    this.pending = true; this.setBusy(true);
    document.getElementById('task-form-error').hidden = true;
    try {
      await work();
      if (version !== this.version) return;
      this.pending = false; this.closeTaskModal(); this.pending = true;
      const loaded = await this.loadTasks();
      if (version !== this.version) return;
      if (loaded) showToast(message, 'success');
    } catch (error) {
      if (version !== this.version) return;
      showToast(error.message, 'error');
      const errorBox = document.getElementById('task-form-error');
      errorBox.textContent = error.message; errorBox.hidden = false;
    } finally {
      if (version === this.version) { this.pending = false; this.setBusy(false); }
    }
  }

  async handleTaskMove(taskId, newStatus) {
    if (this.pending) return;
    const task = this.tasks.find(task => String(task.id) === String(taskId));
    if (!task || task.status === newStatus) return;
    const status = {Todo: 0, InProgress: 1, Completed: 2}[newStatus];
    const position = this.tasks.filter(task => task.status === newStatus).length;
    return this.mutate(() => api.moveTask(taskId, status, position), 'Đã chuyển trạng thái công việc.');
  }

  async handleDeleteTask(taskId) {
    if (this.pending || !this.requireAccount()) return;
    if (!confirm('Xóa công việc này?')) return;
    return this.mutate(() => api.deleteTask(taskId), 'Đã xóa công việc.');
  }

  openTaskModal(defaultStatus = 'Todo', task = null) {
    if (this.pending || !this.requireAccount()) return;
    document.getElementById('task-form-error').hidden = true;
    if (!this.taskModal) return;

    const form = document.getElementById('task-form');
    if (form) form.reset();

    const titleInput = document.getElementById('task-title');
    const descInput = document.getElementById('task-desc');
    const prioritySelect = document.getElementById('task-priority');
    const statusSelect = document.getElementById('task-status');
    const dueDateInput = document.getElementById('task-due-date');
    const modalTitle = document.getElementById('task-modal-title');
    const submitBtn = document.getElementById('task-modal-submit');

    this.currentEditingTaskId = task ? String(task.id) : null;

    if (task) {
      if (modalTitle) modalTitle.textContent = '✏️ Chỉnh Sửa Công Việc';
      if (submitBtn) submitBtn.textContent = 'Cập nhật công việc';
      if (titleInput) titleInput.value = task.title || '';
      if (descInput) descInput.value = task.description || '';
      if (prioritySelect) prioritySelect.value = task.priority || 'Medium';
      if (statusSelect) statusSelect.value = this.normalizeStatus(task.status);
      if (dueDateInput) dueDateInput.value = task.due_date || '';
    } else {
      if (modalTitle) modalTitle.textContent = '+ Tạo Công Việc Mới (Task)';
      if (submitBtn) submitBtn.textContent = 'Lưu công việc';
      if (statusSelect) statusSelect.value = defaultStatus;
    }

    this.taskModal.classList.add('active');

    if (titleInput) {
      setTimeout(() => titleInput.focus(), 100);
    }
  }

  closeTaskModal() {
    if (this.pending) return;
    if (!this.taskModal) return;
    this.currentEditingTaskId = null;
    const form = document.getElementById('task-form');
    if (form) form.reset();
    const modalTitle = document.getElementById('task-modal-title');
    const submitBtn = document.getElementById('task-modal-submit');
    if (modalTitle) modalTitle.textContent = '+ Tạo Công Việc Mới (Task)';
    if (submitBtn) submitBtn.textContent = 'Lưu công việc';
    this.taskModal.classList.remove('active');
  }

  async handleCreateTask() {
    const titleInput = document.getElementById('task-title');
    const descInput = document.getElementById('task-desc');
    const prioritySelect = document.getElementById('task-priority');
    const statusSelect = document.getElementById('task-status');
    const dueDateInput = document.getElementById('task-due-date');

    const title = titleInput?.value.trim() || '';
    const description = descInput?.value.trim() || '';
    const priority = prioritySelect?.value || 'Medium';
    const status = statusSelect?.value || 'Todo';
    const due_date = dueDateInput?.value || null;

    if (!title) {
      showToast('Vui lòng nhập tiêu đề task', 'warning');
      return;
    }

    const payload = { title, description, priority,
      status: {Todo: 0, InProgress: 1, Completed: 2}[status],
      dueDate: due_date ? due_date + 'T00:00:00' : null };
    const id = this.currentEditingTaskId;
    return this.mutate(() => id ? api.updateTask(id, payload) : api.createTask(payload),
      id ? 'Đã cập nhật công việc.' : 'Đã thêm công việc.');
  }

  handleEditTask(taskId) {
    const task = this.tasks.find(t => String(t.id) === String(taskId));
    if (!task) return;
    this.openTaskModal(task.status, task);
  }
}

function startKanban() { if (document.getElementById('tab-content-kanban')) new KanbanBoard().init(); }
if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', startKanban);
else startKanban();
})();
