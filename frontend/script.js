document.addEventListener('DOMContentLoaded', () => {
    // ==========================================================================
    // DEFAULT DATA (Mẫu flashcard ban đầu)
    // ==========================================================================
    const defaultFlashcards = [
        {
            id: 1,
            category: 'programming',
            question: 'API là gì?',
            answer: 'Application Programming Interface (Giao diện lập trình ứng dụng)',
            example: 'Giúp 2 ứng dụng hoặc hệ thống trao đổi dữ liệu với nhau. Ví dụ: Trang web đặt vé máy bay gọi API của hãng bay để lấy giờ bay.',
            status: 'new'
        },
        {
            id: 2,
            category: 'english',
            question: 'Hello / Hi (Basic - Giao Tiếp)',
            answer: 'Xin chào (Cách chào hỏi thông dụng hàng ngày)',
            example: 'Ví dụ: "Hello! How are you today?" (Xin chào! Hôm nay bạn thế nào?)',
            status: 'new'
        },
        {
            id: 3,
            category: 'programming',
            question: 'Biến (Variable) là gì?',
            answer: 'Là một vùng bộ nhớ được đặt tên dùng để lưu trữ dữ liệu.',
            example: 'Ví dụ trong JS: let age = 25; (Trong đó "age" là tên biến và đang lưu giá trị số 25).',
            status: 'new'
        },
        {
            id: 4,
            category: 'general',
            question: 'Sông nào dài nhất thế giới?',
            answer: 'Sông Nile (Sông Nin) ở Châu Phi.',
            example: 'Với chiều dài khoảng 6,650 km, chảy qua 11 quốc gia khác nhau.',
            status: 'new'
        },
        {
            id: 5,
            category: 'english',
            question: 'Thank you / Thanks (Basic - Giao Tiếp)',
            answer: 'Cảm ơn bạn (Cách bày tỏ lòng biết ơn lịch sự)',
            example: 'Ví dụ: "Thank you for your help!" (Cảm ơn vì sự giúp đỡ của bạn!)',
            status: 'new'
        },
        {
            id: 6,
            category: 'english',
            question: 'Excuse me (Basic - Giao Tiếp)',
            answer: 'Xin lỗi cho hỏi... (Dùng để thu hút sự chú ý một cách lịch sự)',
            example: 'Ví dụ: "Excuse me, where is the nearest bus stop?" (Xin hỏi, trạm xe buýt gần nhất ở đâu ạ?)',
            status: 'new'
        },
        {
            id: 7,
            category: 'english',
            question: 'Goodbye / Bye (Basic - Giao Tiếp)',
            answer: 'Tạm biệt (Chào tạm biệt khi ra về)',
            example: 'Ví dụ: "Goodbye! See you again." (Tạm biệt! Hẹn gặp lại bạn sau.)',
            status: 'new'
        },
        {
            id: 8,
            category: 'english',
            question: 'Nice to meet you (Basic - Giao Tiếp)',
            answer: 'Rất vui được gặp bạn (Dùng khi gặp gỡ ai đó lần đầu tiên)',
            example: 'Ví dụ: "Nice to meet you, I am Phan." (Rất vui được gặp bạn, tôi là Phan.)',
            status: 'new'
        },
        {
            id: 9,
            category: 'english',
            question: 'Boarding pass (Intermediate - Du Lịch)',
            answer: 'Thẻ lên máy bay (Giấy tờ cần thiết sau khi làm thủ tục check-in)',
            example: 'Ví dụ: "Please show your boarding pass before entering the plane." (Vui lòng xuất trình thẻ lên máy bay trước khi vào máy bay.)',
            status: 'new'
        },
        {
            id: 10,
            category: 'english',
            question: 'Reservation (Intermediate - Đời Sống)',
            answer: 'Sự đặt chỗ trước (Ví dụ đặt trước bàn ăn, phòng khách sạn...)',
            example: 'Ví dụ: "I have a reservation for a double room." (Tôi có một lịch đặt phòng trước cho phòng đôi.)',
            status: 'new'
        },
        {
            id: 11,
            category: 'english',
            question: 'Luggage / Baggage (Intermediate - Du Lịch)',
            answer: 'Hành lý (Đồ đạc mang theo khi đi du lịch hoặc di chuyển)',
            example: 'Ví dụ: "Do not leave your luggage unattended." (Đừng để hành lý của bạn mà không có người trông coi.)',
            status: 'new'
        },
        {
            id: 12,
            category: 'english',
            question: 'Restroom (Intermediate - Đời Sống)',
            answer: 'Nhà vệ sinh (Từ lịch sự dùng ở nơi công cộng như nhà hàng, sân bay...)',
            example: 'Ví dụ: "Where is the restroom, please?" (Cho tôi hỏi nhà vệ sinh ở đâu ạ?)',
            status: 'new'
        },
        {
            id: 13,
            category: 'english',
            question: 'Currency exchange (Intermediate - Du Lịch)',
            answer: 'Đổi tiền tệ (Nơi hoặc hoạt động đổi tiền mặt nước ngoài)',
            example: 'Ví dụ: "Is there a currency exchange booth near here?" (Có quầy đổi tiền nào ở gần đây không?)',
            status: 'new'
        },
        {
            id: 14,
            category: 'english',
            question: 'Collaborate (Advanced - Công Việc)',
            answer: 'Cộng tác, hợp tác làm việc (Làm việc chung để đạt mục tiêu chung)',
            example: 'Ví dụ: "We need to collaborate to finish the project on time." (Chúng ta cần hợp tác để hoàn thành dự án đúng hạn.)',
            status: 'new'
        },
        {
            id: 15,
            category: 'english',
            question: 'Innovative (Advanced - Công Việc)',
            answer: 'Mang tính đổi mới, sáng tạo đột phá (Ý tưởng mới mẻ và mang lại hiệu quả)',
            example: 'Ví dụ: "The company is famous for its innovative products." (Công ty nổi tiếng với các sản phẩm sáng tạo đột phá.)',
            status: 'new'
        },
        {
            id: 16,
            category: 'english',
            question: 'Feasible (Advanced - Học Thuật)',
            answer: 'Khả thi, có thể thực hiện được (Kế hoạch có khả năng thực hiện thành công)',
            example: 'Ví dụ: "It is a feasible plan that fits our budget." (Đó là một kế hoạch khả thi phù hợp với ngân sách của chúng ta.)',
            status: 'new'
        },
        {
            id: 17,
            category: 'english',
            question: 'Ambiguous (Advanced - Học Thuật)',
            answer: 'Mơ hồ, nhập nhèm, không rõ ràng (Có nhiều hơn một cách hiểu)',
            example: 'Ví dụ: "The instructions were too ambiguous to follow." (Lời chỉ dẫn quá mơ hồ để có thể làm theo.)',
            status: 'new'
        },
        {
            id: 18,
            category: 'english',
            question: 'Comprehensive (Advanced - Học Thuật)',
            answer: 'Toàn diện, bao quát đầy đủ mọi khía cạnh',
            example: 'Ví dụ: "This is a comprehensive guide to learning English." (Đây là cuốn hướng dẫn toàn diện để học tiếng Anh.)',
            status: 'new'
        }
    ];

    // Read cards from LocalStorage or use default cards + Oxford vocabulary
    const storedCards = JSON.parse(localStorage.getItem('flashcards'));
    const oxfordData = typeof OXFORD_VOCAB_DATA !== 'undefined' ? OXFORD_VOCAB_DATA : [];
    
    let flashcards;
    if (storedCards && storedCards.length > 0) {
        flashcards = [...storedCards];
        // Tìm các thẻ mới trong database hệ thống chưa được lưu trong localStorage (ví dụ: các thẻ Python mới)
        const storedIds = new Set(storedCards.map(c => c.id));
        oxfordData.forEach(card => {
            if (!storedIds.has(card.id)) {
                flashcards.push(card);
            }
        });
    } else {
        flashcards = [...defaultFlashcards, ...oxfordData];
    }

    // Initialize Spaced Repetition (SRS) fields for all cards if not present
    flashcards.forEach(card => {
        if (card.repetition === undefined) card.repetition = 0;
        if (card.interval === undefined) card.interval = 1;
        if (card.efactor === undefined) card.efactor = 2.5;
        if (card.nextReviewDate === undefined) card.nextReviewDate = 0;
        if (card.version === undefined) card.version = 1; // #9 – optimistic locking
        if (card.updatedAt === undefined) card.updatedAt = new Date().toISOString();
    });
    
    // #9 – Offline queue for flashcard operations
    const OFFLINE_QUEUE_KEY = 'ms-offline-queue';
    let offlineQueue = JSON.parse(localStorage.getItem(OFFLINE_QUEUE_KEY) || '[]');
    
    function saveOfflineQueue() {
        localStorage.setItem(OFFLINE_QUEUE_KEY, JSON.stringify(offlineQueue));
    }
    
    async function processOfflineQueue() {
        if (!MindSprintApi.isLoggedIn() || offlineQueue.length === 0) return;
        
        const queue = [...offlineQueue];
        offlineQueue = [];
        saveOfflineQueue();
        
        for (const op of queue) {
            try {
                await op.fn();
            } catch (e) {
                // Re-queue failed operations
                offlineQueue.push(op);
            }
        }
        saveOfflineQueue();
    }
    
    // Process queue when online
    window.addEventListener('online', processOfflineQueue);
    
    // Variables for App State
    let currentCategory = 'all';
    let currentSubCategory = 'all';
    let filteredCards = [...flashcards];
    let currentIndex = 0;
    let activeTab = 'flashcards';

    // Quiz State Variables
    let quizDeck = [];
    let quizCurrentIndex = 0;
    let quizScore = 0;
    let quizMode = 'choice';
    let quizWrongAnswers = [];
    let quizCurrentOptions = [];
    let isQuizAnswered = false;

    // Timetable State Variables
    const defaultSlots = [
        { id: 1, day: 2, start: "19:00", end: "20:00", category: "english", note: "Học từ vựng mới" },
        { id: 2, day: 3, start: "20:30", end: "21:30", category: "programming", note: "Luyện code Javascript" },
        { id: 3, day: 4, start: "19:00", end: "20:00", category: "english", note: "Luyện phát âm" },
        { id: 4, day: 5, start: "20:30", end: "21:30", category: "programming", note: "Ôn tập thuật toán" },
        { id: 5, day: 6, start: "19:00", end: "20:00", category: "general", note: "Đọc kiến thức khoa học" },
        { id: 6, day: 7, start: "09:00", end: "11:00", category: "mixed", note: "Làm bài trắc nghiệm tổng hợp" }
    ];
    let timetableSlots = JSON.parse(localStorage.getItem('timetable-slots')) || defaultSlots;

    // Notifications State Variables
    let notificationsEnabled = localStorage.getItem('notifications-enabled') === 'true';
    let notifiedSlotsToday = new Set();
    let lastNotificationDay = new Date().getDate();
    let alarmStyle = localStorage.getItem('alarm-style') || 'both';
    let alarmIntervalId = null;
    let audioCtx = null;
    let editingSlotId = null;

    // Theo dõi số lượng thẻ học đã ôn trong ngày
    let studiedToday = new Set(JSON.parse(localStorage.getItem('studied-today') || '[]'));
    const currentDayStr = new Date().toDateString();
    const lastStudiedDay = localStorage.getItem('last-studied-day');
    if (lastStudiedDay !== currentDayStr) {
        studiedToday.clear();
        localStorage.setItem('studied-today', JSON.stringify([]));
        localStorage.setItem('last-studied-day', currentDayStr);
    }

    // PWA Install Event Handler
    let deferredPrompt;

    // DOM Elements - Sidebar & Tabs
    const navTabs = document.querySelectorAll('.nav-tab');
    const tabPanes = document.querySelectorAll('.tab-pane');
    const sidebarCategories = document.getElementById('sidebar-categories');
    const installBtn = document.getElementById('settings-install-btn');
    const themeToggle = document.getElementById('settings-theme-toggle');

    // DOM Elements - Flashcards Tab
    const flashcard = document.getElementById('flashcard');
    const questionEl = document.getElementById('card-question');
    const answerEl = document.getElementById('card-answer');
    const exampleEl = document.getElementById('card-example');
    const frontCatEl = document.getElementById('card-front-cat');
    const backCatEl = document.getElementById('card-back-cat');
    
    const prevBtn = document.getElementById('prev-btn');
    const nextBtn = document.getElementById('next-btn');
    const progressText = document.getElementById('progress-text');
    const progressBar = document.getElementById('progress-bar');
    
    const statTotal = document.getElementById('stat-total');
    const statKnown = document.getElementById('stat-known');
    const statReview = document.getElementById('stat-review');
    
    const categoryItems = document.querySelectorAll('.category-item');
    const subFilterContainer = document.getElementById('sub-filter-container');
    const subCategorySelect = document.getElementById('topic-options-list');
    const srsToggle = document.getElementById('srs-toggle');
    const srsStatusText = document.getElementById('srs-status-text');
    
    const btnRemember = document.getElementById('btn-remember');
    const btnForget = document.getElementById('btn-forget');

    const addModal = document.getElementById('add-modal');
    const openAddModalBtn = document.getElementById('open-add-modal');
    const closeAddModalBtn = document.getElementById('close-add-modal');
    const cancelAddModalBtn = document.getElementById('cancel-add-modal');
    const addCardForm = document.getElementById('add-card-form');

    // DOM Elements - Quiz Tab
    const quizSetupContainer = document.getElementById('quiz-setup-container');
    const quizOngoingContainer = document.getElementById('quiz-ongoing-container');
    const quizResultsContainer = document.getElementById('quiz-results-container');
    const quizSetupForm = document.getElementById('quiz-setup-form');
    const quizCategorySelect = document.getElementById('quiz-category');
    const quizSubFilterGroup = document.getElementById('quiz-sub-filter-group');
    const quizSubcategorySelect = document.getElementById('quiz-subcategory');
    const quizModeSelect = document.getElementById('quiz-mode');
    const quizLimitSelect = document.getElementById('quiz-limit');
    
    let quizSubDropdownController = null;
    let gameSubDropdownController = null;
    
    const quizQNum = document.getElementById('quiz-q-num');
    const quizQBar = document.getElementById('quiz-q-bar');
    const quizScoreText = document.getElementById('quiz-score');
    const quizQuestionText = document.getElementById('quiz-question-text');
    const quizHintText = document.getElementById('quiz-hint-text');
    const quizCardCat = document.getElementById('quiz-card-cat');
    const quizQuestionLabel = document.getElementById('quiz-question-label');
    const quizExitOngoingBtn = document.getElementById('quiz-exit-ongoing-btn');
    
    const quizOptionsGrid = document.getElementById('quiz-options-grid');
    const quizWritePanel = document.getElementById('quiz-write-panel');
    const quizWriteInput = document.getElementById('quiz-write-input');
    const quizWriteSubmitBtn = document.getElementById('quiz-write-submit-btn');
    
    const quizFeedbackBanner = document.getElementById('quiz-feedback-banner');
    const quizFeedbackText = document.getElementById('quiz-feedback-text');
    const quizNextQBtn = document.getElementById('quiz-next-q-btn');
    
    const resScore = document.getElementById('res-score');
    const resPercent = document.getElementById('res-percent');
    const wrongAnswersSection = document.getElementById('wrong-answers-section');
    const wrongAnswersTbody = document.getElementById('wrong-answers-tbody');
    const quizRetryBtn = document.getElementById('quiz-retry-btn');
    const quizExitBtn = document.getElementById('quiz-exit-btn');

    // DOM Elements - Timetable Tab
    const openTimetableModalBtn = document.getElementById('open-timetable-modal');
    const timetableModal = document.getElementById('timetable-modal');
    const closeTimetableModalBtn = document.getElementById('close-timetable-modal');
    const cancelTimetableModalBtn = document.getElementById('cancel-timetable-modal');
    const timetableForm = document.getElementById('timetable-form');
    const timetableTodayWidget = document.getElementById('timetable-today-widget');
    const timetableWidgetTitle = document.getElementById('timetable-widget-title');
    const timetableWidgetDesc = document.getElementById('timetable-widget-desc');
    const notificationToggleBtn = document.getElementById('notification-toggle-btn');
    const timetableModalTitle = document.getElementById('timetable-modal-title');

    // DOM Elements - Edit Card Modal
    const editModal = document.getElementById('edit-modal');
    const closeEditModalBtn = document.getElementById('close-edit-modal');
    const cancelEditModalBtn = document.getElementById('cancel-edit-modal');
    const editCardForm = document.getElementById('edit-card-form');

    // DOM Elements - Alarm Clock
    const alarmTypeSelect = document.getElementById('alarm-type-select');
    const alarmRingOverlay = document.getElementById('alarm-ring-overlay');
    const alarmTimeText = document.getElementById('alarm-time-text');
    const alarmTitleText = document.getElementById('alarm-title-text');
    const alarmNoteText = document.getElementById('alarm-note-text');
    const alarmStopBtn = document.getElementById('alarm-stop-btn');

    // ==========================================================================
    // INITIALIZATION & SW REGISTER
    // ==========================================================================
    function init() {
        // Register PWA Service Worker
        if ('serviceWorker' in navigator) {
            window.addEventListener('load', () => {
                navigator.serviceWorker.register('./sw.js')
                    .then(reg => console.log('PWA Service Worker registered:', reg.scope))
                    .catch(err => console.log('PWA Service Worker registration failed:', err));
            });
        }

        // Check browser notification permission
        if ('Notification' in window) {
            updateNotificationButtonState();
        }

        // Setup alarm style initial value
        if (alarmTypeSelect) {
            alarmTypeSelect.value = alarmStyle;
        }

        // Gesture unlock for audio context
        const unlockAudio = () => {
            if (!audioCtx) {
                audioCtx = new (window.AudioContext || window.webkitAudioContext)();
            }
            if (audioCtx && audioCtx.state === 'suspended') {
                audioCtx.resume();
            }
        };
        ['click', 'touchstart', 'keydown'].forEach(evt => {
            document.addEventListener(evt, unlockAudio);
        });

        // Đã đăng nhập -> nạp thẻ & tiến độ SRS từ backend (SQL Server)
        if (window.MindSprintApi && MindSprintApi.isLoggedIn()) {
            MindSprintApi.getCards().then(cards => {
                if (Array.isArray(cards) && cards.length) {
                    flashcards.length = 0;
                    flashcards.push(...cards);
                    filterDeck();
                    updateStats();
                }
            }).catch(err => console.warn('Không tải được thẻ từ server:', err.message));

            // #14 – Load study schedule from server
            MindSprintApi.studyGetSchedule().then(slots => {
                if (Array.isArray(slots) && slots.length) {
                    timetableSlots.length = 0;
                    slots.forEach(s => {
                        // Convert server format to frontend format
                        // dayOfWeek: 0=Sun..6=Sat -> dayVal: 2=Mon..8=Sun
                        const dayVal = s.dayOfWeek === 0 ? 8 : s.dayOfWeek + 2;
                        const [hh, mm] = s.startTime.split(':').map(Number);
                        const endMinutes = hh * 60 + mm + s.durationMinutes;
                        const endH = Math.floor(endMinutes / 60);
                        const endM = endMinutes % 60;
                        const startStr = `${String(hh).padStart(2, '0')}:${String(mm).padStart(2, '0')}`;
                        const endStr = `${String(endH).padStart(2, '0')}:${String(endM).padStart(2, '0')}`;
                        
                        timetableSlots.push({
                            id: Date.now() + Math.random(), // temporary, will be replaced
                            serverId: s.id,
                            day: dayVal,
                            start: startStr,
                            end: endStr,
                            category: s.label?.split(' - ')[0] || 'mixed',
                            note: s.label?.includes(' - ') ? s.label.split(' - ').slice(1).join(' - ') : '',
                            durationMinutes: s.durationMinutes,
                            dayOfWeek: s.dayOfWeek
                        });
                    });
                    localStorage.setItem('timetable-slots', JSON.stringify(timetableSlots));
                }
            }).catch(err => console.warn('Không tải được lịch học từ server:', err.message));

            // #14 – Load streak from server
            MindSprintApi.studyGetStreak().then(streak => {
                if (streak) {
                    localStorage.setItem('study-streak', streak.currentStreak.toString());
                    updateStats(); // updates home streak display
                }
            }).catch(err => console.warn('Không tải được streak từ server:', err.message));
        }

        // Setup views
        filterDeck();
        updateStats();
        renderTimetable();
        updateTodayWidget();
        
        // Handle action parameters from notifications
        const urlParams = new URLSearchParams(window.location.search);
        if (urlParams.get('action') === 'disable-notifications') {
            notificationsEnabled = false;
            localStorage.setItem('notifications-enabled', 'false');
            updateNotificationButtonState();
            alert('Đã tắt toàn bộ thông báo nhắc nhở học tập thành công!');
            window.history.replaceState({}, document.title, window.location.pathname);
        }

        // Background timers
        setInterval(updateTodayWidget, 30000);
        setInterval(checkScheduleReminders, 30000);

        setupEventListeners();
        setupQuizListeners();
        setupTimetableListeners();

        quizSubDropdownController = initSearchableDropdown(
            'quiz-sub-trigger', 'quiz-sub-dropdown', 'quiz-sub-search-input', 'quiz-sub-options', 'quiz-sub-selected-text', 'quiz-subcategory'
        );
        gameSubDropdownController = initSearchableDropdown(
            'game-sub-trigger', 'game-sub-dropdown', 'game-sub-search-input', 'game-sub-options', 'game-sub-selected-text', 'game-subcategory'
        );
    }

    // ==========================================================================
    // CORE LAYOUT - TABS NAVIGATION & THEME
    // ==========================================================================
    function switchTab(tabId) {
        activeTab = tabId;
        
        navTabs.forEach(tab => {
            if (tab.getAttribute('data-tab') === tabId) {
                tab.classList.add('active');
            } else {
                tab.classList.remove('active');
            }
        });

        tabPanes.forEach(pane => {
            if (pane.id === `tab-content-${tabId}`) {
                pane.classList.add('active');
            } else {
                pane.classList.remove('active');
            }
        });

        if (tabId === 'flashcards' || tabId === 'quiz') {
            sidebarCategories.style.display = 'block';
        } else {
            sidebarCategories.style.display = 'none';
        }

        if (tabId === 'timetable') {
            updateTodayWidget();
            renderTimetable();
        }

        if (tabId === 'flashcards') {
            renderLibraryDecks();
        }
    }

    function renderLibraryDecks() {
        const englishDecksGrid = document.getElementById('english-decks-grid');
        const itDecksGrid = document.getElementById('it-decks-grid');
        const englishSection = document.getElementById('english-library-section');
        const itSection = document.getElementById('it-library-section');
        const viewContainer = document.getElementById('library-view-container');

        if (!englishDecksGrid || !itDecksGrid || !englishSection || !itSection || !viewContainer) return;

        // Reset hiển thị và dữ liệu cũ
        englishDecksGrid.innerHTML = '';
        itDecksGrid.innerHTML = '';
        englishSection.style.display = 'block';
        itSection.style.display = 'block';
        
        const oldMsg = viewContainer.querySelector('.no-cards-message');
        if (oldMsg) oldMsg.remove();

        // Lọc các thẻ học dựa trên danh mục chính đang hoạt động (english, programming, general, all)
        let activeCategoryCards = currentCategory === 'all' 
            ? flashcards 
            : flashcards.filter(c => c.category === currentCategory);

        // Lọc theo chủ đề chi tiết (subCategory) nếu người dùng chọn cụ thể
        if (currentSubCategory !== 'all') {
            activeCategoryCards = activeCategoryCards.filter(c => c.subCategory === currentSubCategory);
        }

        // Nhóm tất cả thẻ học của danh mục/chủ đề hiện tại theo subCategory để tính toán tổng quan
        const subCategoryGroups = {};
        activeCategoryCards.forEach(card => {
            const sub = card.subCategory || 'Kiến thức chung khác';
            if (!subCategoryGroups[sub]) {
                subCategoryGroups[sub] = [];
            }
            subCategoryGroups[sub].push(card);
        });

        // Nếu bật học ngắt quãng SRS, ta lọc chỉ hiển thị các bộ thẻ có thẻ cần ôn tập hôm nay
        const now = Date.now();
        const dueSubCategoryGroups = {};
        let newCardsLimit = 20;
        let newCardsSeen = 0;
        
        Object.keys(subCategoryGroups).forEach(subName => {
            const allCardsInSub = subCategoryGroups[subName];
            if (srsToggle.checked) {
                // Chỉ lấy các thẻ đã đến hạn ôn tập + thẻ mới giới hạn 20 thẻ tinh
                const dueCards = allCardsInSub.filter(card => {
                    if (card.nextReviewDate > 0) {
                        return card.nextReviewDate <= now;
                    } else {
                        if (newCardsSeen < newCardsLimit) {
                            newCardsSeen++;
                            return true;
                        }
                        return false;
                    }
                });
                
                if (dueCards.length > 0) {
                    dueSubCategoryGroups[subName] = {
                        all: allCardsInSub,
                        due: dueCards
                    };
                }
            } else {
                dueSubCategoryGroups[subName] = {
                    all: allCardsInSub,
                    due: allCardsInSub
                };
            }
        });

        const displayKeys = Object.keys(dueSubCategoryGroups).sort((a, b) => a.localeCompare(b, 'vi'));

        // Tạo giao diện cho mỗi nhóm chủ đề
        displayKeys.forEach(subName => {
            const groupData = dueSubCategoryGroups[subName];
            const totalCount = groupData.all.length;
            const dueCount = groupData.due.length;
            
            // Tính toán mức độ thành thạo trung bình của chủ đề đó (số thẻ đã thuộc / tổng số thẻ)
            const knownCnt = groupData.all.filter(c => c.status === 'known').length;
            const masteryPercent = totalCount > 0 ? Math.round((knownCnt / totalCount) * 100) : 0;
            
            // Xác định màu huy hiệu
            const firstCard = groupData.all[0];
            let badgeClass = 'badge-general';
            let badgeText = 'KIẾN THỨC CHUNG';
            
            if (firstCard.category === 'english') {
                badgeClass = 'badge-english';
                badgeText = 'TIẾNG ANH';
            } else if (firstCard.category === 'programming') {
                badgeClass = 'badge-cntt';
                badgeText = 'LẬP TRÌNH';
            }

            const cardDiv = document.createElement('div');
            cardDiv.className = 'deck-card glass-panel' + (masteryPercent >= 80 ? ' ai-glow-border' : '');
            
            // Nội dung mô tả số thẻ học tùy thuộc vào chế độ SRS có đang được bật hay không
            const cardsInfoText = srsToggle.checked 
                ? `<span style="color: var(--warning-color); font-weight: bold;">${dueCount} thẻ cần ôn lại</span> / ${totalCount} thẻ`
                : `<span>${totalCount} thẻ học</span>`;

            cardDiv.innerHTML = `
                <span class="deck-badge ${badgeClass}">${badgeText}</span>
                <h4 class="deck-title">${subName}</h4>
                <div class="deck-meta">
                    ${cardsInfoText}
                    <span class="suggestion-mastery" style="font-weight: 600; color: var(--primary-color);">Thành thạo ${masteryPercent}%</span>
                </div>
                <div class="deck-progress-bar">
                    <div class="progress" style="width: ${masteryPercent}%; background: var(--primary-color);"></div>
                </div>
                <div class="deck-card-actions">
                    <button class="btn btn-secondary view-deck-btn">Xem thẻ</button>
                    <button class="btn btn-primary quiz-deck-btn">Luyện tập</button>
                </div>
            `;

            // Xử lý sự kiện click nút xem thẻ học (Ôn tập)
            cardDiv.querySelector('.view-deck-btn').addEventListener('click', (e) => {
                e.stopPropagation();
                openStudyModal(subName);
            });

            // Xử lý sự kiện click nút luyện tập (Quiz)
            cardDiv.querySelector('.quiz-deck-btn').addEventListener('click', (e) => {
                e.stopPropagation();
                startQuizForTopic(subName);
            });

            // Phân chia đưa vào đúng Grid tương ứng
            if (firstCard.category === 'english') {
                englishDecksGrid.appendChild(cardDiv);
            } else {
                itDecksGrid.appendChild(cardDiv);
            }
        });

        // Ẩn bớt danh mục nếu không có thẻ nào hiển thị
        if (englishDecksGrid.children.length === 0) {
            englishSection.style.display = 'none';
        }
        if (itDecksGrid.children.length === 0) {
            itSection.style.display = 'none';
        }

        // Trường hợp cả 2 mục đều trống
        if (englishDecksGrid.children.length === 0 && itDecksGrid.children.length === 0) {
            englishSection.style.display = 'none';
            itSection.style.display = 'none';

            const emptyMsgDiv = document.createElement('div');
            emptyMsgDiv.className = 'no-cards-message glass-panel text-center';
            emptyMsgDiv.style.cssText = 'padding: 3rem; border-radius: var(--border-radius-lg); width: 100%;';
            
            if (srsToggle.checked) {
                emptyMsgDiv.innerHTML = `
                    <i class="fas fa-check-circle" style="font-size: 3.5rem; color: var(--success-color); margin-bottom: 1.2rem; display: block;"></i>
                    <h3 style="font-size: 1.3rem; margin-bottom: 0.5rem; color: var(--text-primary);">Hoàn thành mục tiêu SRS hôm nay!</h3>
                    <p style="color: var(--text-secondary); max-width: 500px; margin: 0 auto;">Chúc mừng bạn đã ôn tập hết các thẻ cần học. Hãy quay lại vào ngày mai hoặc tắt chế độ SRS để học tự do.</p>
                `;
            } else {
                emptyMsgDiv.innerHTML = `
                    <i class="fas fa-folder-open" style="font-size: 3.5rem; color: var(--text-muted); margin-bottom: 1.2rem; display: block;"></i>
                    <h3 style="font-size: 1.3rem; margin-bottom: 0.5rem; color: var(--text-primary);">Không có thẻ học nào</h3>
                    <p style="color: var(--text-secondary); max-width: 500px; margin: 0 auto;">Không tìm thấy bộ thẻ nào phù hợp với bộ lọc hiện tại.</p>
                `;
            }
            viewContainer.appendChild(emptyMsgDiv);
        }
    }

    function openStudyModal(subName) {
        currentSubCategory = subName;
        
        // Gọi lại hàm lọc thẻ học theo chủ đề này
        filterDeck();
        
        const modal = document.getElementById('study-overlay-modal');
        const title = document.getElementById('study-modal-title');
        if (title) title.innerText = `Ôn tập bộ thẻ: ${subName}`;
        if (modal) modal.style.display = 'flex';
    }

    function startQuizForTopic(subName) {
        switchTab('quiz');
        
        // Chọn danh mục tương ứng
        if (quizCategorySelect) {
            quizCategorySelect.value = currentCategory;
            // Kích hoạt sự kiện thay đổi để cập nhật dropdown chủ đề chi tiết
            quizCategorySelect.dispatchEvent(new Event('change'));
        }
        
        // Gán chủ đề chi tiết tương ứng sau khi DOM nạp các tùy chọn
        setTimeout(() => {
            if (quizSubcategorySelect) {
                quizSubcategorySelect.value = subName;
            }
        }, 50);
    }

    // ==========================================================================
    // FLASHCARD VIEW - FILTER & RENDER (WITH SPACED REPETITION SRS)
    // ==========================================================================
    function populateSubCategoryDropdown() {
        const activeCategoryCards = currentCategory === 'all' 
            ? flashcards 
            : flashcards.filter(c => c.category === currentCategory);
            
        const subs = [...new Set(activeCategoryCards
            .map(c => c.subCategory)
            .filter(sub => sub))]
            .sort((a, b) => a.localeCompare(b, 'vi'));

        const optionsList = document.getElementById('topic-options-list');
        if (!optionsList) return;

        // Xóa danh sách cũ
        optionsList.innerHTML = '';

        // Thêm tùy chọn "Tất cả chủ đề chi tiết"
        const allItem = document.createElement('li');
        allItem.className = 'option-item' + (currentSubCategory === 'all' ? ' selected' : '');
        allItem.innerHTML = `<span>Tất cả chủ đề chi tiết</span> ${currentSubCategory === 'all' ? '<i class="fas fa-check"></i>' : ''}`;
        allItem.setAttribute('data-value', 'all');
        optionsList.appendChild(allItem);

        subs.forEach(sub => {
            const item = document.createElement('li');
            item.className = 'option-item' + (currentSubCategory === sub ? ' selected' : '');
            item.innerHTML = `<span>${sub}</span> ${currentSubCategory === sub ? '<i class="fas fa-check"></i>' : ''}`;
            item.setAttribute('data-value', sub);
            optionsList.appendChild(item);
        });

        // Đảm bảo chủ đề hiện tại vẫn hợp lệ
        if (!subs.includes(currentSubCategory) && currentSubCategory !== 'all') {
            currentSubCategory = 'all';
        }

        // Cập nhật nhãn của nút Trigger
        const triggerLabel = document.getElementById('topic-select-label');
        if (triggerLabel) {
            triggerLabel.innerText = currentSubCategory === 'all' ? 'Tất cả chủ đề chi tiết' : currentSubCategory;
        }

        // Thêm sự kiện click cho từng mục tùy chọn chủ đề
        optionsList.querySelectorAll('.option-item').forEach(item => {
            item.addEventListener('click', () => {
                const val = item.getAttribute('data-value');
                currentSubCategory = val;
                
                // Ẩn menu dropdown
                document.getElementById('topic-searchable-select').classList.remove('active');
                
                // Lọc lại thẻ học
                filterDeck();
            });
        });

        if (subs.length > 0) {
            subFilterContainer.style.display = 'flex';
        } else {
            subFilterContainer.style.display = 'none';
        }
    }

    function filterDeck() {
        let tempCards = [];
        if (currentCategory === 'all') {
            tempCards = [...flashcards];
        } else {
            tempCards = flashcards.filter(card => card.category === currentCategory);
        }

        populateSubCategoryDropdown();

        if (currentSubCategory !== 'all') {
            tempCards = tempCards.filter(card => card.subCategory === currentSubCategory);
        }

        // Apply Spaced Repetition (SRS) Filter if active
        if (srsToggle.checked) {
            const now = Date.now();
            let newCardsLimit = 20;
            let newCardsSeen = 0;
            
            tempCards = tempCards.filter(card => {
                if (card.nextReviewDate > 0) {
                    // Thẻ đã được học và đã đến hạn ôn tập
                    return card.nextReviewDate <= now;
                } else {
                    // Thẻ mới chưa học (nextReviewDate === 0), giới hạn 20 thẻ/ngày
                    if (newCardsSeen < newCardsLimit) {
                        newCardsSeen++;
                        return true;
                    }
                    return false;
                }
            });
        }
        
        filteredCards = tempCards;
        currentIndex = 0;
        renderCard();
        renderLibraryDecks();
    }

    function renderCard() {
        flashcard.classList.remove('flipped');

        if (filteredCards.length === 0) {
            if (srsToggle.checked) {
                questionEl.innerText = "Chúc mừng! 🎉";
                answerEl.innerText = "Bạn đã hoàn thành tất cả các thẻ cần ôn tập ngắt quãng (SRS) cho ngày hôm nay.";
                exampleEl.innerText = "Hãy quay lại vào ngày mai hoặc tắt chế độ SRS để học tự do.";
            } else {
                questionEl.innerText = "Chưa có thẻ nào trong danh mục này!";
                answerEl.innerText = "Hãy bấm nút 'Tạo thẻ mới' ở góc trên để thêm bài học.";
                exampleEl.innerText = "";
            }
            frontCatEl.innerText = "-";
            backCatEl.innerText = "-";
            progressText.innerText = "Thẻ 0 / 0";
            progressBar.style.width = "0%";
            
            btnRemember.style.display = 'none';
            btnForget.style.display = 'none';
            return;
        }

        btnRemember.style.display = 'inline-flex';
        btnForget.style.display = 'inline-flex';

        const currentCard = filteredCards[currentIndex];
        
        // Tự động điều chỉnh cỡ chữ nếu text quá dài
        if (currentCard.question.length > 50) {
            questionEl.classList.add('text-long');
        } else {
            questionEl.classList.remove('text-long');
        }

        if (currentCard.answer.length > 50) {
            answerEl.classList.add('text-long');
        } else {
            answerEl.classList.remove('text-long');
        }

        questionEl.innerText = currentCard.question;
        const subSuffix = currentCard.subCategory ? ` - ${currentCard.subCategory}` : '';
        frontCatEl.innerText = getCategoryName(currentCard.category) + subSuffix;
        
        answerEl.innerText = currentCard.answer;
        exampleEl.innerText = currentCard.example || "Không có ví dụ.";
        backCatEl.innerText = getCategoryName(currentCard.category) + subSuffix;

        progressText.innerText = `Thẻ ${currentIndex + 1} / ${filteredCards.length}`;
        const percentage = ((currentIndex + 1) / filteredCards.length) * 100;
        progressBar.style.width = `${percentage}%`;

        updateCardFeedbackUI(currentCard.status);
    }

    function updateStats() {
        if (statTotal) statTotal.innerText = flashcards.length;
        
        // Count cards memorized (nextReviewDate > now)
        const now = Date.now();
        const memorized = flashcards.filter(c => c.nextReviewDate > now).length;
        if (statKnown) statKnown.innerText = memorized;
        
        // Cần ôn lại is cards marked as 'review'
        const reviewCount = flashcards.filter(c => c.status === 'review').length;
        if (statReview) statReview.innerText = reviewCount;
        
        // Cập nhật các ô thống kê trực quan trên Trang chủ (Home Tab)
        const homeStreakVal = document.getElementById('home-streak-val');
        const homeAnsweredVal = document.getElementById('home-answered-val');
        const homeAccuracyVal = document.getElementById('home-accuracy-val');
        const homeFavoriteVal = document.getElementById('home-favorite-val');
        const streakStatBox = document.getElementById('streak-stat-box');
        
        const streakCount = parseInt(localStorage.getItem('study-streak') || '0', 10);

        if (homeStreakVal) {
            homeStreakVal.innerText = streakCount + ' Ngày';
        }
        
        // Cập nhật màu sắc và hiệu ứng viền cho ô Streak theo các cột mốc
        if (streakStatBox) {
            streakStatBox.classList.remove('streak-bronze', 'streak-silver', 'streak-gold', 'streak-platinum', 'streak-diamond', 'streak-legendary');
            if (streakCount >= 365) {
                streakStatBox.classList.add('streak-legendary');
            } else if (streakCount >= 300) {
                streakStatBox.classList.add('streak-diamond');
            } else if (streakCount >= 200) {
                streakStatBox.classList.add('streak-platinum');
            } else if (streakCount >= 100) {
                streakStatBox.classList.add('streak-gold');
            } else if (streakCount >= 50) {
                streakStatBox.classList.add('streak-silver');
            } else if (streakCount >= 30) {
                streakStatBox.classList.add('streak-bronze');
            }
        }

        if (homeAnsweredVal) {
            // Hiển thị số lượng thẻ đã lật ôn tập thực tế hôm nay
            homeAnsweredVal.innerText = studiedToday.size + ' Thẻ';
        }
        if (homeAccuracyVal) {
            // Tỷ lệ thuộc từ thực tế
            const totalKnown = flashcards.filter(c => c.status === 'known').length;
            const accuracyPct = flashcards.length > 0 ? Math.round((totalKnown / flashcards.length) * 100) : 88;
            homeAccuracyVal.innerText = accuracyPct + '%';
        }
        if (homeFavoriteVal) {
            // Số lượng bộ thẻ học thực tế trong thư viện
            const subCategories = [...new Set(flashcards.map(c => c.subCategory).filter(s => s))];
            homeFavoriteVal.innerText = subCategories.length + ' Bộ thẻ';
        }
        
        // Đồng bộ hóa với số từ đã ôn tập hôm nay trên tab Tổng quan
        const overviewStudiedToday = document.getElementById('overview-studied-today');
        if (overviewStudiedToday) {
            overviewStudiedToday.innerText = studiedToday.size + ' từ';
        }
        
        localStorage.setItem('flashcards', JSON.stringify(flashcards));
        
        // Cập nhật thống kê Trang chủ và Tổng quan thực tế
        updateOverviewAndSuggestions();
    }

    function updateOverviewAndSuggestions() {
        // Tính toán thống kê theo từng chủ đề chi tiết (subCategory)
        const subCategoryStats = {};
        flashcards.forEach(card => {
            const sub = card.subCategory || 'Kiến thức chung khác';
            if (!subCategoryStats[sub]) {
                subCategoryStats[sub] = { total: 0, known: 0, category: card.category };
            }
            subCategoryStats[sub].total++;
            if (card.status === 'known') {
                subCategoryStats[sub].known++;
            }
        });

        // Sắp xếp danh sách chủ đề
        const subCategoryList = Object.keys(subCategoryStats).map(name => {
            const stats = subCategoryStats[name];
            const pct = stats.total > 0 ? Math.round((stats.known / stats.total) * 100) : 0;
            return { name, pct, ...stats };
        });

        // 1. Cập nhật Điểm thành thạo chung
        const total = flashcards.length;
        const now = Date.now();
        const known = flashcards.filter(c => c.nextReviewDate > now).length;
        const overallMastery = total > 0 ? Math.round((known / total) * 100) : 0;
        
        const overviewScoreEl = document.getElementById('overview-mastery-score');
        if (overviewScoreEl) {
            overviewScoreEl.innerText = `${overallMastery}/100`;
        }

        // 2. Cập nhật chủ đề cần cải thiện (có độ thành thạo thấp nhất nhưng > 0%)
        const needsImprovement = subCategoryList
            .filter(sub => sub.pct < 100)
            .sort((a, b) => a.pct - b.pct)[0];
        
        const improveSubEl = document.getElementById('overview-improve-subject');
        if (improveSubEl) {
            if (needsImprovement) {
                improveSubEl.innerText = `${needsImprovement.name}`;
            } else {
                improveSubEl.innerText = 'Không có (Đã thuộc 100%!)';
            }
        }

        // 3. Cập nhật Độ thành thạo theo ngành học
        const masteryListEl = document.querySelector('.mastery-list');
        if (masteryListEl) {
            masteryListEl.innerHTML = '';
            
            const catStats = {
                'english': { total: 0, known: 0, name: '🇺🇸 Tiếng Anh Oxford' },
                'programming': { total: 0, known: 0, name: '💻 Lập trình Frontend' },
                'general': { total: 0, known: 0, name: '💡 Kiến thức chung' }
            };

            flashcards.forEach(card => {
                if (catStats[card.category]) {
                    catStats[card.category].total++;
                    if (card.status === 'known') {
                        catStats[card.category].known++;
                    }
                }
            });

            Object.keys(catStats).forEach(catKey => {
                const cat = catStats[catKey];
                if (cat.total > 0) {
                    const pct = Math.round((cat.known / cat.total) * 100);
                    const barColor = catKey === 'english' ? '#4b41e1' : (catKey === 'programming' ? '#7c3aed' : '#10b981');
                    const itemDiv = document.createElement('div');
                    itemDiv.className = 'mastery-item';
                    itemDiv.innerHTML = `
                        <div class="mastery-info">
                            <span>${cat.name}</span>
                            <span class="mastery-pct">${pct}%</span>
                        </div>
                        <div class="mastery-bar-container">
                            <div class="mastery-bar" style="width: ${pct}%; background: ${barColor};"></div>
                        </div>
                    `;
                    masteryListEl.appendChild(itemDiv);
                }
            });
        }

        // 4. Cập nhật các bộ thẻ gợi ý trên Trang chủ (lấy tối đa 3 bộ thẻ nhiều từ nhất)
        const homeSuggestionsGrid = document.querySelector('.home-suggestions-grid');
        if (homeSuggestionsGrid) {
            homeSuggestionsGrid.innerHTML = '';
            
            const topSubCategories = subCategoryList
                .sort((a, b) => b.total - a.total)
                .slice(0, 3);

            topSubCategories.forEach(sub => {
                const badgeClass = sub.category === 'english' ? 'badge-english' : (sub.category === 'programming' ? 'badge-cntt' : 'badge-general');
                const badgeText = sub.category === 'english' ? 'TIẾNG ANH' : (sub.category === 'programming' ? 'LẬP TRÌNH' : 'CHUNG');
                
                const cardDiv = document.createElement('div');
                cardDiv.className = 'suggestion-card glass-panel' + (sub.pct >= 80 ? ' ai-glow-border' : '');
                
                cardDiv.innerHTML = `
                    <span class="suggestion-badge ${badgeClass}" style="background: var(--bg-secondary); padding: 0.25rem 0.6rem; border-radius: 6px; font-weight: 700; font-size: 0.75rem; align-self: flex-start; letter-spacing: 0.5px;">${badgeText}</span>
                    <h4 class="suggestion-title" style="font-family: var(--font-heading); font-size: 1.1rem; font-weight: 700; color: var(--text-primary); margin-top: 0.5rem;">${sub.name}</h4>
                    <div class="suggestion-meta" style="display: flex; justify-content: space-between; font-size: 0.85rem; color: var(--text-secondary); margin-top: 0.5rem;">
                        <span>${sub.total} thẻ học</span>
                        <span class="suggestion-mastery" style="font-weight: 600; color: var(--primary-color);">Thành thạo ${sub.pct}%</span>
                    </div>
                    <div class="suggestion-progress-bar" style="width: 100%; height: 6px; background: rgba(255,255,255,0.05); border-radius: 10px; overflow: hidden; margin-top: 0.5rem;">
                        <div class="progress" style="width: ${sub.pct}%; height: 100%; background: var(--primary-color); border-radius: 10px;"></div>
                    </div>
                `;

                // Sự kiện click học nhanh
                cardDiv.addEventListener('click', () => {
                    openStudyModal(sub.name);
                });

                homeSuggestionsGrid.appendChild(cardDiv);
            });
        }
    }

    function getCategoryName(catKey) {
        const mapping = {
            'english': '🇺🇸 Tiếng Anh',
            'programming': '💻 Lập trình',
            'general': '💡 Kiến thức chung',
            'mixed': '📚 Tổng hợp'
        };
        return mapping[catKey] || 'Thẻ học';
    }

    function updateCardFeedbackUI(status) {
        btnRemember.style.boxShadow = 'none';
        btnForget.style.boxShadow = 'none';
        
        if (status === 'known') {
            btnRemember.style.boxShadow = '0 0 15px var(--success-color)';
        } else if (status === 'review') {
            btnForget.style.boxShadow = '0 0 15px var(--danger-color)';
        }
    }

    // Spaced Repetition (SRS) Update Parameters
    function applySpacedRepetition(cardId, score) {
        const card = flashcards.find(c => c.id == cardId);
        if (!card) return;

        // score: 5 = remembered (known), 0 = forgotten (review)
        if (score === 5) {
            // Correct answer
            if (card.repetition === 0) {
                card.interval = 1; // 1 day
                card.repetition = 1;
            } else if (card.repetition === 1) {
                card.interval = 3; // 3 days
                card.repetition = 2;
            } else {
                card.interval = Math.round(card.interval * card.efactor);
                card.repetition = card.repetition + 1;
            }
            card.efactor = Math.max(1.3, card.efactor + 0.1);
            card.nextReviewDate = Date.now() + card.interval * 24 * 60 * 60 * 1000;
            card.status = 'known';
        } else {
            // Incorrect answer
            card.interval = 1;
            card.repetition = 0;
            card.efactor = Math.max(1.3, card.efactor - 0.2);
            card.nextReviewDate = Date.now(); // due immediately
            card.status = 'review';
        }
        if (window.MindSprintApi && MindSprintApi.isLoggedIn()) {
            MindSprintApi.review(card.id, score === 5).catch(err => console.warn('Sync SRS lỗi:', err.message));

            // #14 – Upsert study day (VN date)
            const vnDate = new Date().toLocaleDateString('en-CA', { timeZone: 'Asia/Ho_Chi_Minh' }); // YYYY-MM-DD in VN timezone
            MindSprintApi.studyUpsertDay(vnDate, 1, 1).catch(err => console.warn('Sync study day lỗi:', err.message));
        }
        updateStats();
    }

    // ==========================================================================
    // ACTION LISTENERS - FLASHCARDS
    // ==========================================================================
    function setupEventListeners() {
        navTabs.forEach(tab => {
            tab.addEventListener('click', () => {
                switchTab(tab.getAttribute('data-tab'));
            });
        });

        flashcard.addEventListener('click', () => {
            flashcard.classList.toggle('flipped');
        });

        nextBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            if (filteredCards.length <= 1) return;
            currentIndex = (currentIndex + 1) % filteredCards.length;
            renderCard();
        });

        prevBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            if (filteredCards.length <= 1) return;
            currentIndex = (currentIndex - 1 + filteredCards.length) % filteredCards.length;
            renderCard();
        });

        document.addEventListener('keydown', (e) => {
            if (activeTab === 'quiz' && quizOngoingContainer.style.display !== 'none' && quizMode === 'write') {
                return; 
            }
            if (document.activeElement.tagName === 'INPUT' || document.activeElement.tagName === 'TEXTAREA' || document.activeElement.tagName === 'SELECT') {
                return;
            }

            if (activeTab === 'flashcards') {
                if (e.key === ' ' || e.key === 'Enter') {
                    e.preventDefault();
                    flashcard.classList.toggle('flipped');
                } else if (e.key === 'ArrowRight') {
                    nextBtn.click();
                } else if (e.key === 'ArrowLeft') {
                    prevBtn.click();
                }
            }
        });

        btnRemember.addEventListener('click', (e) => {
            e.stopPropagation();
            const currentCard = filteredCards[currentIndex];
            if (currentCard) {
                applySpacedRepetition(currentCard.id, 5);
                updateCardFeedbackUI('known');
                
                // Ghi nhận số từ đã ôn trong ngày
                studiedToday.add(currentCard.id);
                localStorage.setItem('studied-today', JSON.stringify([...studiedToday]));
                checkAndUpdateStreak(true); // Tự động cập nhật chuỗi streak
                updateStats();
            }
            
            setTimeout(() => {
                // If in SRS mode, card might disappear on filter deck update
                if (srsToggle.checked) {
                    filterDeck();
                } else if (filteredCards.length > 1) {
                    nextBtn.click();
                }
            }, 600);
        });

        btnForget.addEventListener('click', (e) => {
            e.stopPropagation();
            const currentCard = filteredCards[currentIndex];
            if (currentCard) {
                applySpacedRepetition(currentCard.id, 0);
                updateCardFeedbackUI('review');
                
                // Ghi nhận số từ đã ôn trong ngày
                studiedToday.add(currentCard.id);
                localStorage.setItem('studied-today', JSON.stringify([...studiedToday]));
                checkAndUpdateStreak(true); // Tự động cập nhật chuỗi streak
                updateStats();
            }
            
            setTimeout(() => {
                if (srsToggle.checked) {
                    filterDeck();
                } else if (filteredCards.length > 1) {
                    nextBtn.click();
                }
            }, 600);
        });

        // SRS Toggle switch listener
        srsToggle.addEventListener('change', () => {
            if (srsToggle.checked) {
                srsStatusText.innerText = 'Đang bật';
                srsStatusText.classList.add('active');
            } else {
                srsStatusText.innerText = 'Đang tắt';
                srsStatusText.classList.remove('active');
            }
            filterDeck();
        });

        categoryItems.forEach(item => {
            item.addEventListener('click', () => {
                categoryItems.forEach(i => i.classList.remove('active'));
                item.classList.add('active');
                currentCategory = item.getAttribute('data-category');
                currentSubCategory = 'all';
                filterDeck();
            });
        });

        // Thiết lập sự kiện tương tác cho bộ chọn chủ đề tùy chỉnh
        const topicSelectTrigger = document.getElementById('topic-select-trigger');
        const topicSearchableSelect = document.getElementById('topic-searchable-select');
        const topicSearchInput = document.getElementById('topic-search-input');

        if (topicSelectTrigger && topicSearchableSelect) {
            topicSelectTrigger.addEventListener('click', (e) => {
                e.stopPropagation();
                topicSearchableSelect.classList.toggle('active');
                if (topicSearchableSelect.classList.contains('active') && topicSearchInput) {
                    topicSearchInput.value = '';
                    // Kích hoạt lại bộ lọc trống để hiện lại mọi chủ đề
                    topicSearchInput.dispatchEvent(new Event('input'));
                    topicSearchInput.focus();
                }
            });

            // Click ra ngoài để tự động đóng dropdown
            document.addEventListener('click', (e) => {
                if (!topicSearchableSelect.contains(e.target)) {
                    topicSearchableSelect.classList.remove('active');
                }
            });

            // Tìm kiếm chủ đề thời gian thực (Real-time search)
            if (topicSearchInput) {
                const removeVietnameseTones = (str) => {
                    str = str.replace(/à|á|ạ|ả|ã|â|ầ|ấ|ậ|ẩ|ẫ|ă|ằ|ắ|ặ|ẳ|ẵ/g,"a"); 
                    str = str.replace(/è|é|ẹ|ẻ|ẽ|ê|ề|ế|ệ|ể|ễ/g,"e"); 
                    str = str.replace(/ì|í|ị|ỉ|ĩ/g,"i"); 
                    str = str.replace(/ò|ó|ọ|ỏ|õ|ô|ồ|ố|ộ|ổ|ỗ|ơ|ờ|ớ|ợ|ở|ỡ/g,"o"); 
                    str = str.replace(/ù|ú|ụ|ủ|ũ|ư|ừ|ứ|ự|ử|ữ/g,"u"); 
                    str = str.replace(/ỳ|ý|ỵ|ỷ|ỹ/g,"y"); 
                    str = str.replace(/đ/g,"d");
                    str = str.replace(/À|Á|Ạ|Ả|Ã|Â|Ầ|Ấ|Ậ|Ẩ|Ẫ|Ă|Ằ|Ắ|Ặ|Ẳ|Ẵ/g, "A");
                    str = str.replace(/È|É|Ẹ|Ẻ|Ẽ|Ê|Ề|Ế|Ệ|Ể|Ễ/g, "E");
                    str = str.replace(/Ì|Í|Ị|Ỉ|Ĩ/g, "I");
                    str = str.replace(/Ò|Ó|Ọ|B|Õ|Ô|Ồ|Ố|Ộ|Ổ|Ỗ|Ơ|Ờ|Ớ|Ợ|Ở|Ỡ/g, "O");
                    str = str.replace(/Ù|Ý|Ụ|Ủ|Ũ|Ư|Ừ|Ứ|Ự|Ử|Ữ/g, "U");
                    str = str.replace(/Ỳ|Ý|Ỵ|Ỷ|Ỹ/g, "Y");
                    str = str.replace(/Đ/g, "D");
                    str = str.replace(/\u0300|\u0301|\u0303|\u0309|\u0323/g, ""); 
                    str = str.replace(/\u02C6|\u0306|\u031B/g, ""); 
                    return str;
                };

                topicSearchInput.addEventListener('input', (e) => {
                    const query = removeVietnameseTones(e.target.value.toLowerCase().trim());
                    const optionsList = document.getElementById('topic-options-list');
                    if (optionsList) {
                        const options = optionsList.querySelectorAll('.option-item');
                        options.forEach(opt => {
                            const txt = removeVietnameseTones(opt.querySelector('span').innerText.toLowerCase());
                            if (txt.includes(query)) {
                                opt.style.display = 'flex';
                            } else {
                                opt.style.display = 'none';
                            }
                        });
                    }
                });
            }
        }

        themeToggle.addEventListener('click', () => {
            const html = document.documentElement;
            const currentTheme = html.getAttribute('data-theme');
            const newTheme = currentTheme === 'dark' ? 'light' : 'dark';
            
            html.setAttribute('data-theme', newTheme);
            localStorage.setItem('app-theme', newTheme);
            
            const icon = themeToggle.querySelector('i');
            icon.className = newTheme === 'dark' ? 'fas fa-sun' : 'fas fa-moon';
        });

        const savedTheme = localStorage.getItem('app-theme');
        if (savedTheme) {
            document.documentElement.setAttribute('data-theme', savedTheme);
            const icon = themeToggle.querySelector('i');
            icon.className = savedTheme === 'dark' ? 'fas fa-sun' : 'fas fa-moon';
        }

        // Sidebar Toggle action listener for Focused Study Mode
        const sidebarToggleBtn = document.getElementById('sidebar-toggle');
        const appContainer = document.querySelector('.app-container');
        if (sidebarToggleBtn && appContainer) {
            // Restore saved focus mode state
            const savedFocusMode = localStorage.getItem('sidebar-hidden') === 'true';
            if (savedFocusMode) {
                appContainer.classList.add('sidebar-hidden');
                const icon = sidebarToggleBtn.querySelector('i');
                if (icon) icon.className = 'fas fa-outdent';
                sidebarToggleBtn.title = "Hiện thanh bên";
            }

            sidebarToggleBtn.addEventListener('click', () => {
                appContainer.classList.toggle('sidebar-hidden');
                const isHidden = appContainer.classList.contains('sidebar-hidden');
                localStorage.setItem('sidebar-hidden', isHidden ? 'true' : 'false');
                
                const icon = sidebarToggleBtn.querySelector('i');
                if (icon) {
                    icon.className = isHidden ? 'fas fa-outdent' : 'fas fa-indent';
                }
                sidebarToggleBtn.title = isHidden ? "Hiện thanh bên" : "Ẩn thanh bên để tập trung học";
            });
        }

        // Modals management
        openAddModalBtn.addEventListener('click', () => addModal.classList.add('active'));
        closeAddModalBtn.addEventListener('click', () => addModal.classList.remove('active'));
        cancelAddModalBtn.addEventListener('click', () => addModal.classList.remove('active'));

        addCardForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            
            const category = document.getElementById('new-category').value;
            const question = document.getElementById('new-question').value;
            const answer = document.getElementById('new-answer').value;
            const example = document.getElementById('new-example').value;

            const newCard = {
                id: Date.now().toString(),
                category: category,
                question: question,
                answer: answer,
                example: example,
                status: 'new',
                repetition: 0,
                interval: 1,
                efactor: 2.5,
                nextReviewDate: 0,
                version: 1,
                updatedAt: new Date().toISOString()
            };

            // #9 – Sync to server if logged in
            const syncToServer = async () => {
                try {
                    const result = await MindSprintApi.createCard(newCard);
                    newCard.version = result.version;
                    newCard.updatedAt = result.updatedAt;
                } catch (err) {
                    if (err.isConflict) {
                        // Shouldn't happen on create, but handle anyway
                        console.warn('Create conflict:', err);
                    }
                    throw err;
                }
            };

            const doAdd = async () => {
                flashcards.push(newCard);
                addCardForm.reset();
                addModal.classList.remove('active');
                updateStats();
                
                if (currentCategory === 'all' || currentCategory === category) {
                    filterDeck();
                    currentIndex = filteredCards.length - 1;
                    renderCard();
                } else {
                    alert("Đã thêm thẻ mới thành công! Bạn có thể xem trong mục danh mục tương ứng.");
                }
                
                // Sync to server if logged in
                if (MindSprintApi.isLoggedIn()) {
                    try {
                        await syncToServer();
                    } catch (err) {
                        if (!navigator.onLine || err.message.includes('NetworkError') || err.message.includes('Failed to fetch')) {
                            // Queue for later
                            offlineQueue.push({ fn: syncToServer, cardId: newCard.id, type: 'create' });
                            saveOfflineQueue();
                            console.log('Offline: queued card creation');
                        } else {
                            alert('Lỗi đồng bộ: ' + err.message);
                        }
                    }
                }
            };
            
            await doAdd();
        });

        // PWA Installation handling
        window.addEventListener('beforeinstallprompt', (e) => {
            e.preventDefault();
            deferredPrompt = e;
            installBtn.style.display = 'inline-flex';
        });

        installBtn.addEventListener('click', async () => {
            if (deferredPrompt) {
                deferredPrompt.prompt();
                const { outcome } = await deferredPrompt.userChoice;
                console.log(`PWA installation prompt outcome: ${outcome}`);
                deferredPrompt = null;
                installBtn.style.display = 'none';
            }
        });

        window.addEventListener('appinstalled', () => {
            console.log('SmartFlash PWA installed successfully!');
            installBtn.style.display = 'none';
        });

        // Edit & Delete Flashcard Actions
        document.querySelectorAll('.edit-card-btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.stopPropagation();
                openEditCardModal();
            });
        });

        document.querySelectorAll('.delete-card-btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.stopPropagation();
                deleteCurrentCard();
            });
        });

        // Edit Modal controls
        closeEditModalBtn.addEventListener('click', () => editModal.classList.remove('active'));
        cancelEditModalBtn.addEventListener('click', () => editModal.classList.remove('active'));

        editCardForm.addEventListener('submit', (e) => {
            e.preventDefault();
            submitEditCard();
        });

        // Nút mở add-modal ở sidebar
        const sidebarAddCardBtn = document.getElementById('sidebar-add-card-btn');
        if (sidebarAddCardBtn) {
            sidebarAddCardBtn.addEventListener('click', () => {
                addModal.classList.add('active');
            });
        }

        // --- Thiết lập sự kiện cho Tab Trang chủ (Home) ---
        const homeStartQuizBtn = document.getElementById('home-start-quiz-btn');
        const homeCreateDeckBtn = document.getElementById('home-create-deck-btn');
        const homeViewAllDecks = document.getElementById('home-view-all-decks');

        if (homeStartQuizBtn) {
            homeStartQuizBtn.addEventListener('click', () => {
                switchTab('quiz');
            });
        }

        if (homeCreateDeckBtn) {
            homeCreateDeckBtn.addEventListener('click', () => {
                addModal.classList.add('active');
            });
        }

        if (homeViewAllDecks) {
            homeViewAllDecks.addEventListener('click', (e) => {
                e.preventDefault();
                switchTab('flashcards');
            });
        }

        // Tương tác click vào các bộ thẻ flashcard gợi ý trên Trang chủ
        const suggestionCards = document.querySelectorAll('.suggestion-card');
        suggestionCards.forEach(card => {
            card.addEventListener('click', () => {
                const title = card.querySelector('.suggestion-title').innerText;
                let topicName = 'Đồ dùng học tập'; // Mặc định
                if (title.includes('Frontend')) {
                    topicName = 'Lập trình Frontend';
                } else if (title.includes('Cơ sở dữ liệu')) {
                    topicName = 'Cơ sở dữ liệu';
                }
                
                // Mở học tập bộ thẻ này
                openStudyModal(topicName);
            });
        });

        // --- Thiết lập sự kiện cho Tab Cài đặt (Settings) ---
        // Thiết lập âm báo mặc định từ localStorage vào radio
        const selectedAlarm = localStorage.getItem('alarm-style') || 'both';
        const alarmRadios = document.querySelectorAll('input[name="settings-alarm-style"]');
        alarmRadios.forEach(radio => {
            if (radio.value === selectedAlarm) {
                radio.checked = true;
                // Highlight card viền tím
                const card = radio.closest('.alarm-option-card');
                if (card) {
                    card.style.borderColor = 'var(--primary-color)';
                    card.style.background = 'var(--primary-glow)';
                }
            }
            
            radio.addEventListener('change', (e) => {
                alarmStyle = e.target.value;
                localStorage.setItem('alarm-style', alarmStyle);
                
                // Cập nhật giao diện lựa chọn
                alarmRadios.forEach(r => {
                    const card = r.closest('.alarm-option-card');
                    if (card) {
                        if (r.checked) {
                            card.style.borderColor = 'var(--primary-color)';
                            card.style.background = 'var(--primary-glow)';
                        } else {
                            card.style.borderColor = 'var(--border-color)';
                            card.style.background = 'transparent';
                        }
                    }
                });
            });
        });

        // Thiết lập nút đóng cho study-overlay-modal
        const closeStudyModalBtn = document.getElementById('close-study-modal-btn');
        const studyOverlayModal = document.getElementById('study-overlay-modal');
        if (closeStudyModalBtn && studyOverlayModal) {
            closeStudyModalBtn.addEventListener('click', () => {
                studyOverlayModal.style.display = 'none';
                renderLibraryDecks(); // Làm mới tiến trình ở thư viện
            });
        }

        // Bấm tiêu đề thư viện để ẩn/hiện lưới bộ thẻ
        document.querySelectorAll('.library-section-header').forEach(header => {
            header.addEventListener('click', () => {
                const section = header.closest('.library-section');
                if (section) {
                    section.classList.toggle('collapsed');
                }
            });
        });

        // Sidebar logout link
        const sidebarLogout = document.querySelector('.footer-link:has(.fa-sign-out-alt)');
        if (sidebarLogout) {
            sidebarLogout.style.cursor = 'pointer';
            sidebarLogout.addEventListener('click', async () => {
                if (confirm('Bạn có chắc muốn đăng xuất khỏi mọi thiết bị?')) {
                    try {
                        await MindSprintApi.revokeAllSessions();
                    } catch (e) {
                        console.warn('Logout error:', e.message);
                    }
                    MindSprintApi.logout();
                    location.reload();
                }
            });
        }
    }

    function openEditCardModal() {
        if (filteredCards.length === 0) return;
        const currentCard = filteredCards[currentIndex];
        
        document.getElementById('edit-category').value = currentCard.category;
        document.getElementById('edit-question').value = currentCard.question;
        document.getElementById('edit-answer').value = currentCard.answer;
        document.getElementById('edit-example').value = currentCard.example || '';
        
        editModal.classList.add('active');
    }

    function submitEditCard() {
        if (filteredCards.length === 0) return;
        const currentCard = filteredCards[currentIndex];
        
        const cardIndex = flashcards.findIndex(c => c.id == currentCard.id);
        if (cardIndex === -1) return;

        const category = document.getElementById('edit-category').value;
        const question = document.getElementById('edit-question').value;
        const answer = document.getElementById('edit-answer').value;
        const example = document.getElementById('edit-example').value;

        // Store old values for rollback
        const oldValues = {
            category: flashcards[cardIndex].category,
            question: flashcards[cardIndex].question,
            answer: flashcards[cardIndex].answer,
            example: flashcards[cardIndex].example,
            version: flashcards[cardIndex].version,
            updatedAt: flashcards[cardIndex].updatedAt
        };

        // Optimistic update
        flashcards[cardIndex].category = category;
        flashcards[cardIndex].question = question;
        flashcards[cardIndex].answer = answer;
        flashcards[cardIndex].example = example;

        updateStats();
        editModal.classList.remove('active');
        
        let prevIndex = currentIndex;
        filterDeck();
        
        currentIndex = Math.min(prevIndex, filteredCards.length - 1);
        renderCard();

        // #9 – Sync to server if logged in
        if (MindSprintApi.isLoggedIn()) {
            const syncToServer = async () => {
                try {
                    const result = await MindSprintApi.updateCard({
                        ...flashcards[cardIndex],
                        version: flashcards[cardIndex].version
                    });
                    flashcards[cardIndex].version = result.version;
                    flashcards[cardIndex].updatedAt = result.updatedAt;
                } catch (err) {
                    if (err.isConflict) {
                        // Conflict detected - show server version and let user choose
                        const serverCard = err.serverData.serverCard;
                        const userChoice = confirm(
                            `Thẻ này đã được sửa trên thiết bị khác!\n\n` +
                            `Phiên bản server:\n${serverCard.question}\n${serverCard.answer}\n\n` +
                            `Chọn "OK" để giữ bản server, "Cancel" để giữ bản cục bộ và ghi đè.`
                        );
                        
                        if (userChoice) {
                            // Use server version
                            flashcards[cardIndex].category = serverCard.category;
                            flashcards[cardIndex].question = serverCard.question;
                            flashcards[cardIndex].answer = serverCard.answer;
                            flashcards[cardIndex].example = serverCard.example;
                            flashcards[cardIndex].version = serverCard.version;
                            flashcards[cardIndex].updatedAt = serverCard.updatedAt;
                        } else {
                            // Force push local version with server's version number
                            try {
                                const forceResult = await MindSprintApi.updateCard({
                                    ...flashcards[cardIndex],
                                    version: serverCard.version // Use server version to force
                                });
                                flashcards[cardIndex].version = forceResult.version;
                                flashcards[cardIndex].updatedAt = forceResult.updatedAt;
                            } catch (e) {
                                console.error('Force update failed:', e);
                                alert('Không thể ghi đè. Vui lòng thử lại.');
                                // Rollback
                                Object.assign(flashcards[cardIndex], oldValues);
                            }
                        }
                        filterDeck();
                        renderCard();
                        updateStats();
                    } else if (!navigator.onLine || err.message.includes('NetworkError') || err.message.includes('Failed to fetch')) {
                        // Queue for later
                        offlineQueue.push({ 
                            fn: async () => {
                                const result = await MindSprintApi.updateCard({
                                    ...flashcards[cardIndex],
                                    version: flashcards[cardIndex].version
                                });
                                flashcards[cardIndex].version = result.version;
                                flashcards[cardIndex].updatedAt = result.updatedAt;
                            }, 
                            cardId: flashcards[cardIndex].id, 
                            type: 'update' 
                        });
                        saveOfflineQueue();
                        console.log('Offline: queued card update');
                    } else {
                        alert('Lỗi đồng bộ: ' + err.message);
                        // Rollback on other errors
                        Object.assign(flashcards[cardIndex], oldValues);
                        filterDeck();
                        renderCard();
                        updateStats();
                    }
                }
            };
            
            syncToServer();
        }

    function deleteCurrentCard() {
        if (filteredCards.length === 0) return;
        const currentCard = filteredCards[currentIndex];
        const cardVersion = currentCard.version;

        if (confirm(`Bạn có chắc chắn muốn xóa thẻ học này (${currentCard.question}) không?`)) {
            // Store for rollback
            const deletedCard = { ...currentCard };
            const deletedIndex = flashcards.findIndex(c => c.id == currentCard.id);
            
            flashcards = flashcards.filter(c => c.id != currentCard.id);
            updateStats();
            
            let prevIndex = currentIndex;
            filterDeck();
            
            if (filteredCards.length > 0) {
                currentIndex = Math.min(prevIndex, filteredCards.length - 1);
                renderCard();
            } else {
                renderCard();
            }

            // #9 – Sync to server if logged in
            if (MindSprintApi.isLoggedIn()) {
                const syncToServer = async () => {
                    try {
                        await MindSprintApi.deleteCard(currentCard.id, cardVersion);
                    } catch (err) {
                        if (err.isConflict) {
                            // Card was modified, ask user
                            const userChoice = confirm(
                                `Thẻ này đã thay đổi trên thiết bị khác!\n\n` +
                                `Chọn "OK" để xóa theo phiên bản server, "Cancel" để khôi phục thẻ.`
                            );
                            if (userChoice) {
                                // Force delete with server version
                                try {
                                    await MindSprintApi.deleteCard(currentCard.id, err.serverData.serverVersion);
                                } catch (e) {
                                    console.error('Force delete failed:', e);
                                }
                            } else {
                                // Restore card
                                flashcards.splice(deletedIndex, 0, deletedCard);
                                updateStats();
                                filterDeck();
                                renderCard();
                            }
                        } else if (!navigator.onLine || err.message.includes('NetworkError') || err.message.includes('Failed to fetch')) {
                            // Queue for later
                            offlineQueue.push({ 
                                fn: async () => {
                                    await MindSprintApi.deleteCard(currentCard.id, cardVersion);
                                }, 
                                cardId: currentCard.id, 
                                type: 'delete' 
                            });
                            saveOfflineQueue();
                            console.log('Offline: queued card deletion');
                        } else {
                            alert('Lỗi đồng bộ xóa: ' + err.message);
                            // Restore on error
                            flashcards.splice(deletedIndex, 0, deletedCard);
                            updateStats();
                            filterDeck();
                            renderCard();
                        }
                    }
                };
                
                syncToServer();
            }
        }
    }

    // ==========================================================================
    // QUIZ PRACTICE ENGINE
    // ==========================================================================
    function setupQuizListeners() {
        quizCategorySelect.addEventListener('change', (e) => {
            const cat = e.target.value;
            const activeCards = cat === 'all' ? flashcards : flashcards.filter(c => c.category === cat);
            const subs = [...new Set(activeCards.map(c => c.subCategory).filter(sub => sub))]
                .sort((a, b) => a.localeCompare(b, 'vi'));

            if (subs.length > 0) {
                quizSubFilterGroup.style.display = 'block';
                if (quizSubDropdownController) {
                    quizSubDropdownController.updateOptions(subs);
                }
            } else {
                quizSubFilterGroup.style.display = 'none';
                if (quizSubDropdownController) {
                    quizSubDropdownController.updateOptions([]);
                }
            }
        });

        quizSetupForm.addEventListener('submit', (e) => {
            e.preventDefault();
            startQuiz();
        });

        quizWriteSubmitBtn.addEventListener('click', () => {
            checkWriteAnswer();
        });

        quizWriteInput.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                e.preventDefault();
                if (!isQuizAnswered) {
                    checkWriteAnswer();
                } else {
                    nextQuizQuestion();
                }
            }
        });

        quizNextQBtn.addEventListener('click', () => {
            nextQuizQuestion();
        });

        quizRetryBtn.addEventListener('click', () => {
            startQuiz();
        });

        quizExitBtn.addEventListener('click', () => {
            quizResultsContainer.style.display = 'none';
            quizSetupContainer.style.display = 'block';
        });

        // Exit ongoing quiz button
        quizExitOngoingBtn.addEventListener('click', () => {
            if (confirm("Bạn có chắc chắn muốn dừng bài luyện tập hiện tại? Mọi tiến trình sẽ bị hủy.")) {
                quizOngoingContainer.style.display = 'none';
                quizSetupContainer.style.display = 'block';
            }
        });

        // ==========================================================================
        // SEGMENTED CONTROL MODE TOGGLE LISTENERS (QUIZ VS MATCH GAME)
        // ==========================================================================
        const modeToggleButtons = document.querySelectorAll('.mode-toggle-btn');
        const quizPracticeSection = document.getElementById('quiz-practice-section');
        const gamePracticeSection = document.getElementById('game-practice-section');

        modeToggleButtons.forEach(btn => {
            btn.addEventListener('click', () => {
                modeToggleButtons.forEach(b => {
                    b.classList.remove('active');
                    b.style.background = 'transparent';
                    b.style.color = 'var(--text-secondary)';
                    b.style.boxShadow = 'none';
                });

                btn.classList.add('active');
                btn.style.background = 'var(--primary-color)';
                btn.style.color = 'white';
                btn.style.boxShadow = '0 4px 12px var(--primary-glow)';

                const targetMode = btn.getAttribute('data-practice-mode');
                if (targetMode === 'quiz') {
                    quizPracticeSection.style.display = 'block';
                    gamePracticeSection.style.display = 'none';
                    quitGame();
                } else if (targetMode === 'game') {
                    quizPracticeSection.style.display = 'none';
                    gamePracticeSection.style.display = 'block';
                    
                    quizOngoingContainer.style.display = 'none';
                    quizResultsContainer.style.display = 'none';
                    quizSetupContainer.style.display = 'block';
                    
                    resetGameSetup();
                }
            });
        });

        // ==========================================================================
        // GAME ACTION LISTENERS
        // ==========================================================================
        const gameCategorySelect = document.getElementById('game-category');
        const gameSubcategorySelect = document.getElementById('game-subcategory');
        const gameSubFilterGroup = document.getElementById('game-sub-filter-group');

        if (gameCategorySelect && gameSubFilterGroup) {
            gameCategorySelect.addEventListener('change', (e) => {
                const cat = e.target.value;
                const activeCards = cat === 'all' ? flashcards : flashcards.filter(c => c.category === cat);
                const subs = [...new Set(activeCards.map(c => c.subCategory).filter(sub => sub))]
                    .sort((a, b) => a.localeCompare(b, 'vi'));

                if (subs.length > 0) {
                    gameSubFilterGroup.style.display = 'block';
                    if (gameSubDropdownController) {
                        gameSubDropdownController.updateOptions(subs);
                    }
                } else {
                    gameSubFilterGroup.style.display = 'none';
                    if (gameSubDropdownController) {
                        gameSubDropdownController.updateOptions([]);
                    }
                }
            });
        }

        const gameStartBtn = document.getElementById('game-start-btn');
        const gameQuitBtn = document.getElementById('game-quit-btn');
        const gameRestartBtn = document.getElementById('game-restart-btn');
        const gameExitBtn = document.getElementById('game-exit-btn');

        if (gameStartBtn) {
            gameStartBtn.addEventListener('click', () => startGame());
        }
        if (gameQuitBtn) {
            gameQuitBtn.addEventListener('click', () => {
                if (confirm("Bạn có chắc chắn muốn thoát trò chơi ghép thẻ? Mọi tiến trình sẽ bị hủy.")) {
                    quitGame();
                }
            });
        }
        if (gameRestartBtn) {
            gameRestartBtn.addEventListener('click', () => startGame());
        }
        if (gameExitBtn) {
            gameExitBtn.addEventListener('click', () => resetGameSetup());
        }
    }

    function startQuiz() {
        const cat = quizCategorySelect.value;
        const sub = quizSubcategorySelect.value;
        quizMode = quizModeSelect.value;
        const limit = quizLimitSelect.value;

        let cards = [];
        if (cat === 'all') {
            cards = [...flashcards];
        } else {
            cards = flashcards.filter(c => c.category === cat);
        }

        if (sub !== 'all') {
            cards = cards.filter(c => c.subCategory === sub);
        }

        if (cards.length === 0) {
            alert("Không tìm thấy từ vựng nào phù hợp trong danh mục học này để ôn tập!");
            return;
        }

        cards = cards.sort(() => 0.5 - Math.random());

        if (limit !== 'all') {
            const count = parseInt(limit, 10);
            cards = cards.slice(0, count);
        }

        quizDeck = cards;
        quizCurrentIndex = 0;
        quizScore = 0;
        quizWrongAnswers = [];
        
        quizSetupContainer.style.display = 'none';
        quizResultsContainer.style.display = 'none';
        quizOngoingContainer.style.display = 'block';

        renderQuizQuestion();
    }

    function renderQuizQuestion() {
        isQuizAnswered = false;
        quizFeedbackBanner.style.display = 'none';
        quizFeedbackBanner.className = 'quiz-feedback-banner';
        quizWriteInput.value = '';

        const currentCard = quizDeck[quizCurrentIndex];
        
        quizQNum.innerText = `Câu hỏi ${quizCurrentIndex + 1} / ${quizDeck.length}`;
        quizQBar.style.width = `${((quizCurrentIndex) / quizDeck.length) * 100}%`;
        quizScoreText.innerText = quizScore;

        const subSuffix = currentCard.subCategory ? ` - ${currentCard.subCategory}` : '';
        quizCardCat.innerText = getCategoryName(currentCard.category) + subSuffix;

        if (quizMode === 'choice') {
            quizQuestionLabel.innerText = "Chọn nghĩa phù hợp cho từ:";
            quizQuestionText.innerText = currentCard.question;
            quizHintText.innerText = currentCard.example ? `Gợi ý: ${currentCard.example}` : '';
            
            quizOptionsGrid.style.display = 'grid';
            quizWritePanel.style.display = 'none';

            generateMultipleChoices(currentCard);
        } else {
            quizQuestionLabel.innerText = "Gõ từ vựng tiếng Anh tương ứng với nghĩa:";
            quizQuestionText.innerText = currentCard.answer;
            
            let hint = '';
            if (currentCard.example) {
                hint += currentCard.example;
            } else {
                hint += "Gợi ý: Từ bắt đầu bằng ký tự '" + currentCard.question.charAt(0) + "'";
            }
            quizHintText.innerText = hint;

            quizOptionsGrid.style.display = 'none';
            quizWritePanel.style.display = 'flex';
            setTimeout(() => quizWriteInput.focus(), 100);
        }
    }

    function generateMultipleChoices(currentCard) {
        const answers = new Set();
        answers.add(currentCard.answer);

        const distractors = flashcards.filter(c => c.category === currentCard.category && c.answer !== currentCard.answer);
        const shuffledDistractors = [...distractors].sort(() => 0.5 - Math.random());

        for (let i = 0; i < shuffledDistractors.length && answers.size < 4; i++) {
            answers.add(shuffledDistractors[i].answer);
        }

        if (answers.size < 4) {
            const allOthers = [...flashcards].sort(() => 0.5 - Math.random());
            for (let i = 0; i < allOthers.length && answers.size < 4; i++) {
                if (allOthers[i].answer !== currentCard.answer) {
                    answers.add(allOthers[i].answer);
                }
            }
        }

        quizCurrentOptions = Array.from(answers).sort(() => 0.5 - Math.random());

        quizOptionsGrid.innerHTML = '';
        const prefixes = ['A', 'B', 'C', 'D'];
        quizCurrentOptions.forEach((opt, idx) => {
            const btn = document.createElement('button');
            btn.className = 'quiz-option-btn';
            btn.innerHTML = `<span class="option-prefix">${prefixes[idx]}</span> ${opt}`;
            btn.addEventListener('click', () => {
                if (!isQuizAnswered) checkChoiceAnswer(idx, btn);
            });
            quizOptionsGrid.appendChild(btn);
        });
    }

    function checkChoiceAnswer(selectedIndex, clickedBtn) {
        isQuizAnswered = true;
        const currentCard = quizDeck[quizCurrentIndex];
        const selectedVal = quizCurrentOptions[selectedIndex];
        const correctVal = currentCard.answer;

        const buttons = quizOptionsGrid.querySelectorAll('.quiz-option-btn');
        buttons.forEach((btn, idx) => {
            if (quizCurrentOptions[idx] === correctVal) {
                btn.classList.add('correct');
            }
        });

        const dbCard = flashcards.find(c => c.id === currentCard.id);

        if (selectedVal === correctVal) {
            quizScore++;
            quizScoreText.innerText = quizScore;
            showQuizFeedback(true, "Chính xác! Làm tốt lắm.");
            
            if (dbCard) {
                dbCard.status = 'known';
                dbCard.repetition = (dbCard.repetition || 0) + 1;
                dbCard.interval = dbCard.repetition === 1 ? 1 : (dbCard.repetition === 2 ? 6 : Math.round(dbCard.interval * dbCard.efactor));
                dbCard.nextReviewDate = Date.now() + dbCard.interval * 24 * 60 * 60 * 1000;
            }
        } else {
            clickedBtn.classList.add('wrong');
            quizWrongAnswers.push({
                question: currentCard.question,
                userAnswer: selectedVal,
                correctAnswer: correctVal
            });
            showQuizFeedback(false, `Sai rồi! Đáp án đúng là: ${correctVal}`);
            
            if (dbCard) {
                dbCard.status = 'review';
                dbCard.repetition = 0;
                dbCard.interval = 1;
                dbCard.nextReviewDate = 0;
            }
        }

        // Đồng bộ hóa thống kê học tập hôm nay
        studiedToday.add(currentCard.id);
        localStorage.setItem('studied-today', JSON.stringify([...studiedToday]));
        checkAndUpdateStreak(true);
        updateStats();
    }

    function checkWriteAnswer() {
        const inputVal = quizWriteInput.value.trim();
        if (!inputVal) return;

        isQuizAnswered = true;
        const currentCard = quizDeck[quizCurrentIndex];
        const correctVal = currentCard.question.trim();

        // Normalize text comparisons
        const inputNorm = inputVal.toLowerCase().replace(/[^a-zA-Z0-9\s]/g, "");
        const correctNorm = correctVal.toLowerCase().replace(/[^a-zA-Z0-9\s]/g, "");

        const dbCard = flashcards.find(c => c.id === currentCard.id);

        if (inputNorm === correctNorm) {
            quizScore++;
            quizScoreText.innerText = quizScore;
            showQuizFeedback(true, "Chính xác! Bạn viết chuẩn rồi.");
            
            if (dbCard) {
                dbCard.status = 'known';
                dbCard.repetition = (dbCard.repetition || 0) + 1;
                dbCard.interval = dbCard.repetition === 1 ? 1 : (dbCard.repetition === 2 ? 6 : Math.round(dbCard.interval * dbCard.efactor));
                dbCard.nextReviewDate = Date.now() + dbCard.interval * 24 * 60 * 60 * 1000;
            }
        } else {
            quizWrongAnswers.push({
                question: currentCard.answer,
                userAnswer: inputVal,
                correctAnswer: correctVal
            });
            showQuizFeedback(false, `Sai rồi! Từ đúng phải viết là: ${correctVal}`);
            
            if (dbCard) {
                dbCard.status = 'review';
                dbCard.repetition = 0;
                dbCard.interval = 1;
                dbCard.nextReviewDate = 0;
            }
        }

        // Đồng bộ hóa thống kê học tập hôm nay
        studiedToday.add(currentCard.id);
        localStorage.setItem('studied-today', JSON.stringify([...studiedToday]));
        checkAndUpdateStreak(true);
        updateStats();
    }

    function showQuizFeedback(isCorrect, message) {
        quizFeedbackText.innerText = message;
        quizFeedbackBanner.className = `quiz-feedback-banner ${isCorrect ? 'success-banner' : 'error-banner'}`;
        quizFeedbackBanner.style.display = 'flex';
    }

    function nextQuizQuestion() {
        quizCurrentIndex++;
        if (quizCurrentIndex < quizDeck.length) {
            renderQuizQuestion();
        } else {
            showQuizResults();
        }
    }

    // Save results and display
    function showQuizResults() {
        quizOngoingContainer.style.display = 'none';
        quizResultsContainer.style.display = 'block';

        resScore.innerText = `${quizScore} / ${quizDeck.length}`;
        const percent = Math.round((quizScore / quizDeck.length) * 100);
        resPercent.innerText = `${percent}%`;

        if (quizWrongAnswers.length > 0) {
            wrongAnswersSection.style.display = 'block';
            wrongAnswersTbody.innerHTML = '';
            quizWrongAnswers.forEach(item => {
                const tr = document.createElement('tr');
                tr.innerHTML = `
                    <td><strong>${item.question}</strong></td>
                    <td class="text-danger">${item.userAnswer || '(Bỏ trống)'}</td>
                    <td class="text-success">${item.correctAnswer}</td>
                `;
                wrongAnswersTbody.appendChild(tr);
            });
        } else {
            wrongAnswersSection.style.display = 'none';
        }
    }

    // ==========================================================================
    // TIMETABLE PLANNER LOGIC (MA TRẬN LỊCH HỌC)
    // ==========================================================================
    function setupTimetableListeners() {
        openTimetableModalBtn.addEventListener('click', () => {
            editingSlotId = null;
            timetableModalTitle.innerText = "Thêm Giờ Học Mới";
            timetableForm.reset();
            timetableModal.classList.add('active');
        });
        closeTimetableModalBtn.addEventListener('click', () => timetableModal.classList.remove('active'));
        cancelTimetableModalBtn.addEventListener('click', () => timetableModal.classList.remove('active'));

        timetableForm.addEventListener('submit', async (e) => {
            e.preventDefault();

            const dayVal = parseInt(document.getElementById('slot-day').value, 10);
            const startVal = document.getElementById('slot-start').value;
            const endVal = document.getElementById('slot-end').value;
            const catVal = document.getElementById('slot-category').value;
            const noteVal = document.getElementById('slot-note').value;

            if (startVal >= endVal) {
                alert("Lỗi: Giờ kết thúc phải muộn hơn giờ bắt đầu học!");
                return;
            }

            // Calculate duration in minutes
            const [sh, sm] = startVal.split(':').map(Number);
            const [eh, em] = endVal.split(':').map(Number);
            const durationMinutes = (eh * 60 + em) - (sh * 60 + sm);

            // Map dayVal (2=Mon..8=Sun) to DayOfWeek (0=Sun..6=Sat)
            const dayOfWeek = dayVal === 8 ? 0 : dayVal;

            const newSlot = {
                day: dayVal,
                start: startVal,
                end: endVal,
                category: catVal,
                note: noteVal,
                durationMinutes: durationMinutes,
                dayOfWeek: dayOfWeek
            };

            if (editingSlotId === null) {
                // Add new slot
                newSlot.id = Date.now();
                timetableSlots.push(newSlot);
            } else {
                // Edit existing slot
                const idx = timetableSlots.findIndex(s => s.id === editingSlotId);
                if (idx !== -1) {
                    timetableSlots[idx] = { ...timetableSlots[idx], ...newSlot };
                }
            }

            localStorage.setItem('timetable-slots', JSON.stringify(timetableSlots));
            
            timetableForm.reset();
            timetableModal.classList.remove('active');
            
            renderTimetable();
            updateTodayWidget();

            // #14 – Sync to server if logged in
            if (MindSprintApi.isLoggedIn()) {
                try {
                    if (editingSlotId === null) {
                        // Create new schedule on server
                        const result = await MindSprintApi.studyCreateSchedule(dayOfWeek, startVal, durationMinutes, catVal + (noteVal ? ' - ' + noteVal : ''));
                        // Update local slot with server ID
                        const localSlot = timetableSlots.find(s => s.id === newSlot.id);
                        if (localSlot) localSlot.serverId = result.id;
                    } else {
                        // Update existing schedule on server
                        const localSlot = timetableSlots.find(s => s.id === editingSlotId);
                        if (localSlot?.serverId) {
                            await MindSprintApi.studyUpdateSchedule(localSlot.serverId, dayOfWeek, startVal, durationMinutes, catVal + (noteVal ? ' - ' + noteVal : ''));
                        }
                    }
                    localStorage.setItem('timetable-slots', JSON.stringify(timetableSlots));
                } catch (err) {
                    console.warn('Lỗi đồng bộ lịch học:', err.message);
                    // Queue for offline sync if needed
                }
            }
            
            editingSlotId = null;
        });

        // Toggle browser notifications settings
        notificationToggleBtn.addEventListener('click', () => {
            toggleNotifications();
        });

        // Alarm style changes
        alarmTypeSelect.addEventListener('change', (e) => {
            alarmStyle = e.target.value;
            localStorage.setItem('alarm-style', alarmStyle);
        });

        // Stop alarm ringing
        alarmStopBtn.addEventListener('click', () => {
            stopAlarm();
            alarmRingOverlay.style.display = 'none';
            switchTab('flashcards');
        });
    }

    function timeToDecimal(timeStr) {
        if (!timeStr) return 0;
        const [h, m] = timeStr.split(':').map(Number);
        return h + (m || 0) / 60;
    }

    function renderTimetable() {
        // Clear all day grid column bodies in the calendar matrix
        for (let day = 2; day <= 8; day++) {
            const bodyEl = document.getElementById(`grid-day-${day}`);
            if (bodyEl) bodyEl.innerHTML = '';
        }

        // Render each slot absolutely based on start and duration times
        // Base range starts at 06:00 (base = 6) and height is 50px per hour
        const baseHour = 6;
        const rowHeight = 50;

        timetableSlots.forEach(slot => {
            const bodyEl = document.getElementById(`grid-day-${slot.day}`);
            if (!bodyEl) return;

            const startDec = timeToDecimal(slot.start);
            const endDec = timeToDecimal(slot.end);
            const duration = endDec - startDec;

            // Calculate absolute top and height values
            const topPx = (startDec - baseHour) * rowHeight;
            const heightPx = duration * rowHeight;

            // Create slot element
            const slotDiv = document.createElement('div');
            slotDiv.className = `study-slot cat-${slot.category}`;
            slotDiv.style.top = `${topPx}px`;
            slotDiv.style.height = `${heightPx}px`;
            slotDiv.title = "Nhấp để chỉnh sửa giờ học này";

            // Click event to edit
            slotDiv.addEventListener('click', () => {
                openEditTimetableModal(slot);
            });

            const catName = getCategoryName(slot.category);
            const noteText = slot.note ? `<span class="slot-desc" title="${slot.note}">${slot.note}</span>` : '';

            slotDiv.innerHTML = `
                <span class="slot-time"><i class="far fa-clock"></i> ${slot.start} - ${slot.end}</span>
                <span class="slot-title" title="${catName}">${catName}</span>
                ${noteText}
                <button class="delete-slot-btn" aria-label="Xóa giờ học"><i class="fas fa-trash"></i></button>
            `;

            // Delete event handler
            const delBtn = slotDiv.querySelector('.delete-slot-btn');
            delBtn.addEventListener('click', async (e) => {
                e.stopPropagation();
                if (confirm(`Bạn có muốn xóa giờ học (${slot.start} - ${slot.end}) khỏi lịch trình không?`)) {
                    // #14 – Delete from server if logged in
                    if (MindSprintApi.isLoggedIn() && slot.serverId) {
                        try {
                            await MindSprintApi.studyDeleteSchedule(slot.serverId);
                        } catch (err) {
                            console.warn('Lỗi xóa lịch học trên server:', err.message);
                        }
                    }
                    timetableSlots = timetableSlots.filter(s => s.id !== slot.id);
                    localStorage.setItem('timetable-slots', JSON.stringify(timetableSlots));
                    renderTimetable();
                    updateTodayWidget();
                }
            });

            bodyEl.appendChild(slotDiv);
        });
    }

    function openEditTimetableModal(slot) {
        editingSlotId = slot.id;
        timetableModalTitle.innerText = "Chỉnh Sửa Giờ Học";
        
        document.getElementById('slot-day').value = slot.day;
        document.getElementById('slot-start').value = slot.start;
        document.getElementById('slot-end').value = slot.end;
        document.getElementById('slot-category').value = slot.category;
        document.getElementById('slot-note').value = slot.note || '';
        
        timetableModal.classList.add('active');
    }

    function updateTodayWidget() {
        const now = new Date();
        const jsDay = now.getDay(); 
        const currentDay = jsDay === 0 ? 8 : jsDay + 1; // Map Sunday to 8

        const hours = String(now.getHours()).padStart(2, '0');
        const minutes = String(now.getMinutes()).padStart(2, '0');
        const currentTime = `${hours}:${minutes}`;

        const todaySlots = timetableSlots
            .filter(s => s.day === currentDay)
            .sort((a, b) => a.start.localeCompare(b.start));

        if (todaySlots.length === 0) {
            timetableWidgetTitle.innerText = "Không có lịch học hôm nay";
            timetableWidgetDesc.innerText = "Bạn không có khung giờ học nào được xếp lịch. Hãy nghỉ ngơi hoặc tự ôn tập tự do nhé!";
            return;
        }

        const activeSlot = todaySlots.find(s => currentTime >= s.start && currentTime <= s.end);

        if (activeSlot) {
            const catName = getCategoryName(activeSlot.category);
            const noteText = activeSlot.note ? ` (${activeSlot.note})` : '';
            timetableWidgetTitle.innerText = `ĐANG TRONG GIỜ HỌC! (${activeSlot.start} - ${activeSlot.end})`;
            timetableWidgetDesc.innerText = `Chủ đề: ${catName}${noteText}. Hãy mở thẻ học ôn tập ngay nào!`;
            timetableTodayWidget.style.borderLeftColor = 'var(--success-color)';
            return;
        }

        const upcomingSlot = todaySlots.find(s => s.start > currentTime);

        if (upcomingSlot) {
            const catName = getCategoryName(upcomingSlot.category);
            timetableWidgetTitle.innerText = `Lịch học tiếp theo: ${upcomingSlot.start} - ${upcomingSlot.end}`;
            timetableWidgetDesc.innerText = `Chủ đề học sắp tới: ${catName}. Hãy chuẩn bị sẵn sàng ôn tập!`;
            timetableTodayWidget.style.borderLeftColor = 'var(--secondary-color)';
        } else {
            timetableWidgetTitle.innerText = "Đã hoàn thành lịch học hôm nay";
            timetableWidgetDesc.innerText = "Tuyệt vời! Bạn đã hoàn thành tất cả các khung giờ ôn tập của ngày hôm nay.";
            timetableTodayWidget.style.borderLeftColor = 'var(--primary-color)';
        }
    }

    // ==========================================================================
    // NOTIFICATIONS SYSTEM & ALARM CLOCK
    // ==========================================================================
    function showNotification(title, options) {
        if ('serviceWorker' in navigator && 'showNotification' in ServiceWorkerRegistration.prototype) {
            navigator.serviceWorker.ready.then(registration => {
                registration.showNotification(title, options);
            }).catch(err => {
                console.log('SW Notification failed, fallback to main thread:', err);
                if ('Notification' in window) {
                    new Notification(title, options);
                }
            });
        } else if ('Notification' in window) {
            new Notification(title, options);
        }
    }

    function playSyntheticAlarm() {
        if (alarmIntervalId) return; // already ringing
        
        if (!audioCtx) {
            audioCtx = new (window.AudioContext || window.webkitAudioContext)();
        }
        
        if (audioCtx.state === 'suspended') {
            audioCtx.resume();
        }
        
        // Ring sound: double beeps every 1 second
        alarmIntervalId = setInterval(() => {
            playBeep(880, 0.12);
            setTimeout(() => playBeep(880, 0.12), 150);
        }, 1000);
    }

    function playBeep(frequency, duration) {
        if (!audioCtx) return;
        try {
            const osc = audioCtx.createOscillator();
            const gain = audioCtx.createGain();
            
            osc.type = 'sine';
            osc.frequency.setValueAtTime(frequency, audioCtx.currentTime);
            
            gain.gain.setValueAtTime(0.3, audioCtx.currentTime);
            gain.gain.exponentialRampToValueAtTime(0.01, audioCtx.currentTime + duration);
            
            osc.connect(gain);
            gain.connect(audioCtx.destination);
            
            osc.start();
            osc.stop(audioCtx.currentTime + duration);
        } catch (e) {
            console.error('Audio beep failed:', e);
        }
    }

    function stopAlarm() {
        if (alarmIntervalId) {
            clearInterval(alarmIntervalId);
            alarmIntervalId = null;
        }
    }

    function toggleNotifications() {
        if (!('Notification' in window)) {
            alert('Trình duyệt của bạn không hỗ trợ tính năng thông báo!');
            return;
        }
        
        if (notificationsEnabled) {
            notificationsEnabled = false;
            localStorage.setItem('notifications-enabled', 'false');
            updateNotificationButtonState();
        } else {
            if (Notification.permission === 'granted') {
                notificationsEnabled = true;
                localStorage.setItem('notifications-enabled', 'true');
                updateNotificationButtonState();
            } else if (Notification.permission === 'denied') {
                alert('Bạn đã chặn quyền thông báo của trang web này. Vui lòng nhấp vào biểu tượng ổ khóa ở đầu thanh địa chỉ trình duyệt, chọn Cho phép (Allow) thông báo rồi thử lại!');
            } else {
                Notification.requestPermission().then(permission => {
                    if (permission === 'granted') {
                        notificationsEnabled = true;
                        localStorage.setItem('notifications-enabled', 'true');
                        showNotification('SmartFlash', {
                            body: 'Đã kích hoạt thông báo nhắc nhở lịch học thành công!',
                            icon: 'icon-192.jpg'
                        });
                    } else {
                        notificationsEnabled = false;
                        localStorage.setItem('notifications-enabled', 'false');
                    }
                    updateNotificationButtonState();
                });
            }
        }
    }

    function updateNotificationButtonState() {
        if (!('Notification' in window)) return;
        
        const btn = document.getElementById('notification-toggle-btn');
        const span = btn.querySelector('span');
        const icon = btn.querySelector('i');
        
        if (notificationsEnabled && Notification.permission === 'granted') {
            btn.classList.remove('btn-secondary');
            btn.classList.add('btn-primary');
            span.innerText = 'Thông báo: Bật';
            icon.className = 'fas fa-bell';
        } else {
            if (Notification.permission === 'denied') {
                notificationsEnabled = false;
                localStorage.setItem('notifications-enabled', 'false');
            }
            btn.classList.remove('btn-primary');
            btn.classList.add('btn-secondary');
            span.innerText = 'Bật thông báo';
            icon.className = 'fas fa-bell-slash';
        }
    }

    function checkScheduleReminders() {
        if (!notificationsEnabled) return;
        
        const now = new Date();
        const date = now.getDate();
        
        // Reset notified list at start of new day
        if (date !== lastNotificationDay) {
            notifiedSlotsToday.clear();
            lastNotificationDay = date;
        }
        
        const jsDay = now.getDay();
        const currentDay = jsDay === 0 ? 8 : jsDay + 1;
        
        const hours = String(now.getHours()).padStart(2, '0');
        const minutes = String(now.getMinutes()).padStart(2, '0');
        const currentTime = `${hours}:${minutes}`;
        
        const todaySlots = timetableSlots.filter(s => s.day === currentDay);
        
        todaySlots.forEach(slot => {
            // So sánh khoảng thời gian (lớn hơn hoặc bằng giờ bắt đầu và nhỏ hơn giờ kết thúc)
            // Việc này giúp tránh bị hụt thông báo khi điện thoại bị delay/sleep trình duyệt
            if (currentTime >= slot.start && currentTime < slot.end && !notifiedSlotsToday.has(slot.id)) {
                notifiedSlotsToday.add(slot.id);
                
                const catName = getCategoryName(slot.category);
                const noteText = slot.note ? ` - Ghi chú: ${slot.note}` : '';
                
                // 1. Text Notification
                if (alarmStyle === 'text' || alarmStyle === 'both') {
                    showNotification('Đến giờ học SmartFlash rồi!', {
                        body: `Lịch học: ${slot.start} - ${slot.end} (${catName}${noteText}). Nhấp vào để học ngay!`,
                        icon: 'icon-192.jpg',
                        requireInteraction: true,
                        actions: [
                            { action: 'study', title: 'Vào học' },
                            { action: 'disable', title: 'Tắt thông báo' }
                        ]
                    });
                }
                
                // 2. Sound Alarm Overlay
                if (alarmStyle === 'sound' || alarmStyle === 'both') {
                    alarmTimeText.innerText = `${slot.start} - ${slot.end}`;
                    alarmTitleText.innerText = catName;
                    alarmNoteText.innerText = slot.note || 'Không có ghi chú';
                    
                    alarmRingOverlay.style.display = 'flex';
                    playSyntheticAlarm();
                }
            }
        });
    }

    // ==========================================================================
    // STREAK AUTO-INCREMENT & MILESTONE CELEBRATION LOGIC
    // ==========================================================================

    function checkAndUpdateStreak(actionTaken) {
        const today = new Date();
        const todayStr = today.toLocaleDateString('en-CA'); // Định dạng YYYY-MM-DD
        
        let lastActiveDate = localStorage.getItem('last-active-date');
        let streak = parseInt(localStorage.getItem('study-streak') || '0', 10);
        
        if (!actionTaken) {
            // Kiểm tra khi tải trang xem chuỗi streak có bị đứt hay không
            if (lastActiveDate) {
                const lastDate = new Date(lastActiveDate);
                const diffTime = Math.abs(today - lastDate);
                const diffDays = Math.ceil(diffTime / (1000 * 60 * 60 * 24));
                
                const yesterday = new Date(today);
                yesterday.setDate(today.getDate() - 1);
                const yesterdayStr = yesterday.toLocaleDateString('en-CA');
                
                // Nếu ngày hoạt động cuối cùng không phải hôm nay và không phải hôm qua -> mất streak
                if (lastActiveDate !== todayStr && lastActiveDate !== yesterdayStr) {
                    streak = 0;
                    localStorage.setItem('study-streak', '0');
                }
            } else {
                streak = 0;
                localStorage.setItem('study-streak', '0');
            }
            return streak;
        }
        
        // Khi người dùng thực hiện học bài hoặc trả lời quiz:
        if (lastActiveDate === todayStr) {
            // Đã hoạt động hôm nay rồi, không tăng thêm nữa
            return streak;
        }
        
        const yesterday = new Date(today);
        yesterday.setDate(today.getDate() - 1);
        const yesterdayStr = yesterday.toLocaleDateString('en-CA');
        
        if (lastActiveDate === yesterdayStr) {
            // Học liên tục -> Tăng streak
            streak += 1;
        } else {
            // Đứt streak hoặc lần đầu học -> Bắt đầu lại mốc 1
            streak = 1;
        }
        
        localStorage.setItem('study-streak', streak.toString());
        localStorage.setItem('last-active-date', todayStr);
        
        // Kiểm tra xem có trúng mốc cột mốc nào để chúc mừng không
        checkStreakMilestones(streak);
        
        return streak;
    }

    function checkStreakMilestones(streak) {
        // Các cột mốc chúc mừng nổi bật
        const milestones = [30, 50, 100, 200, 300, 365, 400, 500, 600, 700, 800, 900, 1000];
        
        if (milestones.includes(streak)) {
            const celebratedMilestones = JSON.parse(localStorage.getItem('celebrated-milestones') || '[]');
            if (!celebratedMilestones.includes(streak)) {
                celebrateMilestone(streak);
                celebratedMilestones.push(streak);
                localStorage.setItem('celebrated-milestones', JSON.stringify(celebratedMilestones));
            }
        }
    }

    function celebrateMilestone(streak) {
        const modal = document.getElementById('streak-milestone-modal');
        const titleEl = document.getElementById('streak-milestone-title');
        const descEl = document.getElementById('streak-milestone-desc');
        const iconEl = document.querySelector('.streak-celebration-icon');
        
        if (!modal) return;
        
        let title = `Mốc Chuỗi ${streak} Ngày!`;
        let desc = `Bạn đã kiên trì học tập liên tiếp ${streak} ngày. Flashcard tự hào về sự bền bỉ của bạn!`;
        let color = '#7c3aed'; // Màu mặc định: Tím
        let iconClass = 'fa-fire';
        
        if (streak >= 365) {
            title = `👑 HUYỀN THOẠI 365 NGÀY! 👑`;
            desc = `Không thể tin được! Bạn đã duy trì thói quen học tập ròng rã suốt 1 năm (${streak} ngày) liên tục! Bạn đã trở thành BẬC THẦY VÔ SONG của Flashcard!`;
            color = '#ff007f'; // Màu hồng bảy sắc
            iconClass = 'fa-crown';
        } else if (streak >= 300) {
            title = `💎 KIM CƯƠNG BẤT HOẠI ${streak} NGÀY! 💎`;
            desc = `Ý chí bền bỉ tựa Kim Cương! Bạn đã học tập liên tục ${streak} ngày. Trí tuệ của bạn đã đạt đến độ sáng ngời tối thượng!`;
            color = '#b9f2ff'; // Xanh kim cương
            iconClass = 'fa-gem';
        } else if (streak >= 200) {
            title = `✨ CHIẾN BINH BẠCH KIM ${streak} NGÀY! ✨`;
            desc = `Một thành tựu phi thường! ${streak} ngày bền bỉ cùng tri thức. Flashcard xin ngả mũ thán phục sự kiên định của bạn!`;
            color = '#e5e4e2'; // Bạch kim
            iconClass = 'fa-medal';
        } else if (streak >= 100) {
            title = `🏆 ĐẠI KIỆN TƯỚNG VÀNG ${streak} NGÀY! 🏆`;
            desc = `Cột mốc 100 ngày thần kỳ! Con số 3 chữ số này là minh chứng đanh thép cho sự quyết tâm sắt đá của bạn. Tiếp tục tiến bước vinh quang!`;
            color = '#ffd700'; // Vàng
            iconClass = 'fa-trophy';
        } else if (streak >= 50) {
            title = `🥈 CHIẾN BINH BẠC ${streak} NGÀY! 🥈`;
            desc = `Chúc mừng bạn đã cán mốc 50 ngày học tập liên tục! Thói quen học tập của bạn hiện tại đã vô cùng vững chắc.`;
            color = '#c0c0c0'; // Bạc
            iconClass = 'fa-award';
        } else if (streak >= 30) {
            title = `🥉 DẤU ẤN ĐỒNG ${streak} NGÀY! 🥉`;
            desc = `Chúc mừng bạn đã hoàn thành xuất sắc thử thách 30 ngày học tập liên tục đầu tiên! Một cột mốc khởi đầu vô cùng ý nghĩa.`;
            color = '#cd7f32'; // Đồng
            iconClass = 'fa-certificate';
        }
        
        if (titleEl) titleEl.innerText = title;
        if (descEl) descEl.innerText = desc;
        if (iconEl) {
            iconEl.className = `fas ${iconClass} streak-celebration-icon`;
            iconEl.style.color = color;
            iconEl.style.filter = `drop-shadow(0 0 20px ${color})`;
        }
        
        modal.style.display = 'flex';
        
        // Tạo pháo hoa hoa giấy giấy confetti
        triggerConfetti();
    }

    function triggerConfetti() {
        const colors = ['#ffd700', '#ff007f', '#7f00ff', '#00f0ff', '#10b981', '#ff5722'];
        for (let i = 0; i < 100; i++) {
            const confetti = document.createElement('div');
            confetti.className = 'confetti-piece';
            
            confetti.style.left = Math.random() * 100 + 'vw';
            confetti.style.backgroundColor = colors[Math.floor(Math.random() * colors.length)];
            confetti.style.width = Math.random() * 8 + 6 + 'px';
            confetti.style.height = confetti.style.width;
            
            const delay = Math.random() * 1.5;
            const duration = Math.random() * 2 + 1.5;
            confetti.style.animationDelay = delay + 's';
            confetti.style.animationDuration = duration + 's';
            
            if (Math.random() > 0.5) {
                confetti.style.borderRadius = '50%';
            }
            
            document.body.appendChild(confetti);
            
            setTimeout(() => {
                confetti.remove();
            }, (delay + duration) * 1000);
        }
    }

    function setupStreakEventListeners() {
        // Nút đóng modal vinh danh
        const closeStreakModalBtn = document.getElementById('close-streak-modal');
        const streakMilestoneModal = document.getElementById('streak-milestone-modal');
        const streakMilestoneClaimBtn = document.getElementById('streak-milestone-claim-btn');
        
        const closeModal = () => {
            if (streakMilestoneModal) streakMilestoneModal.style.display = 'none';
        };
        
        if (closeStreakModalBtn) {
            closeStreakModalBtn.addEventListener('click', closeModal);
        }
        if (streakMilestoneClaimBtn) {
            streakMilestoneClaimBtn.addEventListener('click', closeModal);
        }

        // Cấu hình mở khóa mật mã cho trình giả lập Streak (mật khẩu: 29062007)
        const simUnlockBtn = document.getElementById('sim-unlock-btn');
        const simPwInput = document.getElementById('sim-pw-input');
        const simUnlockContainer = document.getElementById('sim-unlock-container');
        const simControlsContainer = document.getElementById('sim-controls-container');
        const simLockBadge = document.getElementById('sim-lock-badge');

        if (simUnlockBtn && simPwInput) {
            simUnlockBtn.addEventListener('click', () => {
                const enteredPw = simPwInput.value.trim();
                if (enteredPw === '29062007') {
                    // Mở khóa thành công
                    simUnlockContainer.style.display = 'none';
                    simControlsContainer.style.display = 'flex';
                    if (simLockBadge) {
                        simLockBadge.innerHTML = '<i class="fas fa-lock-open"></i> Đã mở khóa';
                        simLockBadge.style.background = 'rgba(16, 185, 129, 0.15)';
                        simLockBadge.style.color = '#10b981';
                    }
                } else {
                    alert("Mật khẩu quản trị viên không chính xác!");
                    simPwInput.value = '';
                    simPwInput.focus();
                }
            });
            simPwInput.addEventListener('keydown', (e) => {
                if (e.key === 'Enter') {
                    simUnlockBtn.click();
                }
            });
        }

        // Trình giả lập Streak (Dành cho việc kiểm tra cột mốc lập tức)
        const testStreakInput = document.getElementById('test-streak-input');
        const testStreakBtn = document.getElementById('test-streak-btn');
        
        if (testStreakBtn && testStreakInput) {
            testStreakBtn.addEventListener('click', () => {
                const targetDays = parseInt(testStreakInput.value, 10);
                if (isNaN(targetDays) || targetDays < 1) {
                    alert("Vui lòng nhập số ngày hợp lệ!");
                    return;
                }
                
                // Cập nhật chuỗi giả lập vào LocalStorage
                localStorage.setItem('study-streak', targetDays.toString());
                localStorage.setItem('last-active-date', new Date().toLocaleDateString('en-CA'));
                
                // Đồng bộ và tải lại giao diện
                updateStats();
                
                // Bỏ qua điều kiện chặn để kích hoạt modal chúc mừng ngay lập tức
                celebrateMilestone(targetDays);
            });
        }
    }

    // ==========================================================================
    // TÍNH NĂNG NHẮC NHỞ HỌC BÙ (MAKEUP STUDY ALERT) CHO IOS & ANDROID
    // ==========================================================================
    
    function checkMakeupStudyAlert() {
        const now = new Date();
        const jsDay = now.getDay();
        const currentDay = jsDay === 0 ? 8 : jsDay + 1; // 2 đến 8 (Thứ 2 đến Chủ nhật)
        
        const hours = String(now.getHours()).padStart(2, '0');
        const minutes = String(now.getMinutes()).padStart(2, '0');
        const currentTime = `${hours}:${minutes}`;
        
        const todayStr = now.toLocaleDateString('en-CA'); // Định dạng YYYY-MM-DD
        const lastCheckedStr = localStorage.getItem('last-makeup-check-time');
        
        // Lưu thời điểm kiểm tra hiện tại vào localStorage
        localStorage.setItem('last-makeup-check-time', `${todayStr} ${currentTime}`);
        
        if (!lastCheckedStr) {
            // Lần đầu chạy app hoặc đã bị xóa cache, không kiểm tra học bù
            return;
        }
        
        const parts = lastCheckedStr.split(' ');
        const lastDate = parts[0];
        const lastTime = parts[1];
        
        // Chỉ kiểm tra học bù trong cùng ngày để tránh bị spam dồn lịch cũ
        if (lastDate !== todayStr) {
            return;
        }
        
        // Lấy tất cả lịch học của ngày hôm nay
        const todaySlots = timetableSlots.filter(s => s.day === currentDay);
        
        // Tìm các slot học đã trôi qua trong khoảng thời gian từ lúc tắt app đến lúc mở app
        const missedSlots = todaySlots.filter(slot => {
            // Lịch học đã bắt đầu sau thời điểm kiểm tra cuối và đã kết thúc trước/bằng thời điểm hiện tại
            return slot.end > lastTime && slot.end <= currentTime;
        });
        
        if (missedSlots.length > 0) {
            // Lấy slot học bị lỡ gần nhất để thông báo
            const slot = missedSlots[0];
            const catName = getCategoryName(slot.category);
            
            // Hiện hộp thoại vinh danh học bù sau 1.5 giây để tránh giật lag khi tải trang
            setTimeout(() => {
                showMakeupModal(catName, slot.start, slot.end);
            }, 1500);
        }
    }

    function showMakeupModal(categoryName, start, end) {
        // Tạo container cho modal học bù
        const overlay = document.createElement('div');
        overlay.className = 'modal-overlay';
        overlay.style.cssText = 'display: flex; z-index: 3500; position: fixed; top: 0; left: 0; width: 100vw; height: 100vh; background: rgba(0,0,0,0.6); justify-content: center; align-items: center; backdrop-filter: blur(10px); -webkit-backdrop-filter: blur(10px);';
        
        const content = document.createElement('div');
        content.className = 'modal-content glass-panel text-center';
        content.style.cssText = 'max-width: 420px; padding: 2.2rem; border-radius: var(--border-radius-lg); position: relative; margin: auto; border: 1px solid var(--border-color); animation: fadeIn 0.3s ease, slideUp 0.3s ease;';
        
        content.innerHTML = `
            <button class="close-btn" style="position: absolute; top: 1rem; right: 1rem; background: transparent; border: none; font-size: 1.4rem; color: var(--text-muted); cursor: pointer;" type="button"><i class="fas fa-times"></i></button>
            <div style="margin-bottom: 1.2rem; display: inline-block;">
                <i class="fas fa-clock-rotate-left" style="font-size: 3.5rem; color: #ffd700; filter: drop-shadow(0 0 10px rgba(255,215,0,0.4));"></i>
            </div>
            <h2 style="font-family: var(--font-heading); font-size: 1.5rem; font-weight: 800; margin-bottom: 0.8rem; color: var(--text-primary); text-transform: uppercase; letter-spacing: 0.5px;">🔔 Lịch Học Bù</h2>
            <p style="color: var(--text-secondary); line-height: 1.6; margin-bottom: 1.8rem; font-size: 0.95rem;">
                Hệ thống phát hiện bạn đã bỏ lỡ giờ học môn <strong>${categoryName}</strong> lúc <strong>${start} - ${end}</strong> hôm nay do thiết bị khóa màn hình. Hãy học bù ngay nhé!
            </p>
            <button class="btn btn-primary btn-large" style="width: 100%; display: flex; align-items: center; justify-content: center; gap: 0.5rem;" id="makeup-study-now-btn" type="button">
                <i class="fas fa-book-reader"></i> Học bù ngay bây giờ
            </button>
        `;
        
        overlay.appendChild(content);
        document.body.appendChild(overlay);
        
        // Đóng nút
        const closeBtn = content.querySelector('.close-btn');
        closeBtn.addEventListener('click', () => overlay.remove());
        
        // Vào học ngay
        const studyBtn = content.querySelector('#makeup-study-now-btn');
        studyBtn.addEventListener('click', () => {
            overlay.remove();
            // Chuyển sang tab Luyện tập (Practice Tab)
            const practiceTab = document.querySelector('[data-tab="practice"]');
            if (practiceTab) practiceTab.click();
        });
    }

    // ==========================================================================
    // GAME GHÉP CẶP TỪ VỰNG - MEMORY MATCH PAIRS GAME LOGIC
    // ==========================================================================
    let gameDeck = [];
    let gameTimerInterval = null;
    let gameTime = 0;
    let gameMatchesCount = 0;
    let selectedCards = [];
    let audioCtxGame = null;

    function playSound(type) {
        try {
            if (!audioCtxGame) {
                audioCtxGame = new (window.AudioContext || window.webkitAudioContext)();
            }
            if (audioCtxGame.state === 'suspended') {
                audioCtxGame.resume();
            }
            
            const osc = audioCtxGame.createOscillator();
            const gain = audioCtxGame.createGain();
            osc.connect(gain);
            gain.connect(audioCtxGame.destination);
            
            const now = audioCtxGame.currentTime;
            
            if (type === 'click') {
                osc.frequency.setValueAtTime(600, now);
                gain.gain.setValueAtTime(0.08, now);
                gain.gain.exponentialRampToValueAtTime(0.01, now + 0.1);
                osc.start(now);
                osc.stop(now + 0.1);
            } else if (type === 'match') {
                osc.frequency.setValueAtTime(523.25, now); // C5
                osc.frequency.setValueAtTime(659.25, now + 0.08); // E5
                osc.frequency.setValueAtTime(783.99, now + 0.16); // G5
                gain.gain.setValueAtTime(0.12, now);
                gain.gain.exponentialRampToValueAtTime(0.01, now + 0.35);
                osc.start(now);
                osc.stop(now + 0.35);
            } else if (type === 'mismatch') {
                osc.frequency.setValueAtTime(220, now); // A3
                osc.frequency.linearRampToValueAtTime(150, now + 0.2);
                gain.gain.setValueAtTime(0.12, now);
                gain.gain.exponentialRampToValueAtTime(0.01, now + 0.2);
                osc.start(now);
                osc.stop(now + 0.2);
            } else if (type === 'victory') {
                const notes = [523.25, 659.25, 783.99, 1046.5]; // C5, E5, G5, C6
                notes.forEach((freq, idx) => {
                    const o = audioCtxGame.createOscillator();
                    const g = audioCtxGame.createGain();
                    o.connect(g);
                    g.connect(audioCtxGame.destination);
                    o.frequency.setValueAtTime(freq, now + idx * 0.1);
                    g.gain.setValueAtTime(0.1, now + idx * 0.1);
                    g.gain.exponentialRampToValueAtTime(0.01, now + idx * 0.1 + 0.25);
                    o.start(now + idx * 0.1);
                    o.stop(now + idx * 0.1 + 0.25);
                });
            }
        } catch (e) {
            console.log("Audio not supported or locked: ", e);
        }
    }

    function resetGameSetup() {
        const gameSetupContainer = document.getElementById('game-setup-container');
        const gameBoardContainer = document.getElementById('game-board-container');
        const gameVictoryContainer = document.getElementById('game-victory-container');

        if (gameSetupContainer) gameSetupContainer.style.display = 'block';
        if (gameBoardContainer) gameBoardContainer.style.display = 'none';
        if (gameVictoryContainer) gameVictoryContainer.style.display = 'none';
        
        clearInterval(gameTimerInterval);
        gameTimerInterval = null;
        selectedCards = [];
    }

    function quitGame() {
        resetGameSetup();
    }

    function startGame() {
        const category = document.getElementById('game-category').value;
        const subcategory = document.getElementById('game-subcategory').value;
        
        // Lọc thẻ theo Danh mục chính
        let filtered = category === 'all' 
            ? [...flashcards] 
            : flashcards.filter(c => c.category === category);
            
        // Lọc thêm theo Chủ đề chi tiết (nếu có chọn chủ đề cụ thể)
        if (category !== 'all' && subcategory !== 'all') {
            filtered = filtered.filter(c => c.subCategory === subcategory);
        }
            
        if (filtered.length < 3) {
            alert("Số từ vựng trong chủ đề này quá ít (yêu cầu tối thiểu 3 từ). Vui lòng chọn danh mục/chủ đề khác hoặc thêm thẻ học!");
            return;
        }

        // Chọn tối đa 6 cặp từ ngẫu nhiên
        const pairsCount = Math.min(6, filtered.length);
        const shuffled = [...filtered].sort(() => 0.5 - Math.random());
        gameDeck = shuffled.slice(0, pairsCount);

        // Tạo mảnh ghép 2 bên (Trái: Từ vựng, Phải: Ý nghĩa)
        let leftItems = [];
        let rightItems = [];
        
        gameDeck.forEach(card => {
            leftItems.push({ id: card.id, text: card.question, type: 'term' });
            rightItems.push({ id: card.id, text: card.answer, type: 'meaning' });
        });

        // Xáo trộn độc lập hai bên để không bị thẳng hàng hàng ngang
        leftItems.sort(() => 0.5 - Math.random());
        rightItems.sort(() => 0.5 - Math.random());

        // Lấy hai cột hiển thị
        const leftGrid = document.getElementById('game-left-grid');
        const rightGrid = document.getElementById('game-right-grid');
        
        leftGrid.innerHTML = '';
        rightGrid.innerHTML = '';

        // Render cột Trái (Từ vựng)
        leftItems.forEach(item => {
            const cardEl = document.createElement('div');
            cardEl.className = 'game-card';
            cardEl.innerText = item.text;
            cardEl.setAttribute('data-id', item.id);
            cardEl.setAttribute('data-type', item.type);

            // Điều chỉnh linh hoạt cỡ chữ cho đoạn văn dài
            if (item.text.length > 100) {
                cardEl.style.fontSize = '0.75rem';
                cardEl.style.lineHeight = '1.3';
                cardEl.style.padding = '0.5rem 0.6rem';
            } else if (item.text.length > 40) {
                cardEl.style.fontSize = '0.85rem';
                cardEl.style.lineHeight = '1.4';
                cardEl.style.padding = '0.6rem 0.8rem';
            }

            cardEl.addEventListener('click', () => {
                handleGameCardClick(item, cardEl);
            });

            leftGrid.appendChild(cardEl);
        });

        // Render cột Phải (Ý nghĩa)
        rightItems.forEach(item => {
            const cardEl = document.createElement('div');
            cardEl.className = 'game-card';
            cardEl.innerText = item.text;
            cardEl.setAttribute('data-id', item.id);
            cardEl.setAttribute('data-type', item.type);

            // Điều chỉnh linh hoạt cỡ chữ cho đoạn văn dài
            if (item.text.length > 100) {
                cardEl.style.fontSize = '0.75rem';
                cardEl.style.lineHeight = '1.3';
                cardEl.style.padding = '0.5rem 0.6rem';
            } else if (item.text.length > 40) {
                cardEl.style.fontSize = '0.85rem';
                cardEl.style.lineHeight = '1.4';
                cardEl.style.padding = '0.6rem 0.8rem';
            }

            cardEl.addEventListener('click', () => {
                handleGameCardClick(item, cardEl);
            });

            rightGrid.appendChild(cardEl);
        });

        // Cập nhật giao diện Trạng thái bắt đầu
        const gameSetupContainer = document.getElementById('game-setup-container');
        const gameBoardContainer = document.getElementById('game-board-container');
        const gameVictoryContainer = document.getElementById('game-victory-container');

        if (gameSetupContainer) gameSetupContainer.style.display = 'none';
        if (gameVictoryContainer) gameVictoryContainer.style.display = 'none';
        if (gameBoardContainer) gameBoardContainer.style.display = 'block';

        // Khởi động đồng hồ bấm giờ
        gameTime = 0;
        gameMatchesCount = 0;
        selectedCards = [];
        
        const timerVal = document.getElementById('game-timer-val');
        if (timerVal) timerVal.innerText = '0.0';

        clearInterval(gameTimerInterval);
        gameTimerInterval = setInterval(() => {
            gameTime += 0.1;
            if (timerVal) timerVal.innerText = gameTime.toFixed(1);
        }, 100);

        // Hiển thị kỷ lục tốt nhất từ trước đến nay của danh mục & chủ đề này
        const recordVal = document.getElementById('game-best-time-val');
        const savedBest = localStorage.getItem(`game-best-time-${category}-${subcategory}`);
        if (recordVal) {
            recordVal.innerText = savedBest ? savedBest : '--';
        }
    }

    function handleGameCardClick(item, element) {
        // Tránh nhấp đúp hoặc nhấp vào thẻ đã khớp
        if (element.classList.contains('selected') || element.classList.contains('matched') || selectedCards.length >= 2) {
            return;
        }

        playSound('click');
        element.classList.add('selected');
        selectedCards.push({ item, element });

        if (selectedCards.length === 2) {
            const card1 = selectedCards[0];
            const card2 = selectedCards[1];

            // So sánh xem có khớp ID (cùng 1 thẻ gốc) và khác loại (1 từ - 1 nghĩa) hay không
            if (card1.item.id === card2.item.id && card1.item.type !== card2.item.type) {
                // Khớp chính xác!
                gameMatchesCount++;
                
                // Gỡ class selected ngay lập tức để không thể nhấp lại
                card1.element.classList.remove('selected');
                card2.element.classList.remove('selected');
                card1.element.classList.add('matched');
                card2.element.classList.add('matched');

                setTimeout(() => {
                    playSound('match');
                    selectedCards = [];

                    // Kiểm tra điều kiện chiến thắng bằng cách đếm số thẻ chưa khớp thực tế trên giao diện
                    const unmatchedCards = document.querySelectorAll('.game-card:not(.matched)');
                    if (unmatchedCards.length === 0) {
                        endGame(true);
                    }
                }, 300);
            } else {
                // Trả lời sai (không khớp)
                setTimeout(() => {
                    playSound('mismatch');
                    card1.element.classList.add('mismatch');
                    card2.element.classList.add('mismatch');

                    setTimeout(() => {
                        card1.element.classList.remove('selected', 'mismatch');
                        card2.element.classList.remove('selected', 'mismatch');
                        selectedCards = [];
                    }, 500);
                }, 300);
            }
        }
    }

    function endGame(isVictory) {
        try {
            clearInterval(gameTimerInterval);
            
            if (isVictory) {
                try {
                    playSound('victory');
                } catch(e) {}
                
                let category = 'all';
                let subcategory = 'all';
                try {
                    category = document.getElementById('game-category').value;
                    subcategory = document.getElementById('game-subcategory').value;
                } catch(e) {}

                const savedBest = localStorage.getItem(`game-best-time-${category}-${subcategory}`);
                const bestTime = savedBest ? parseFloat(savedBest) : Infinity;

                const yourTimeEl = document.getElementById('game-your-time');
                const recordTimeEl = document.getElementById('game-record-time');
                const newRecordBadge = document.getElementById('new-record-badge');

                if (yourTimeEl) yourTimeEl.innerText = gameTime.toFixed(1) + 's';

                if (gameTime < bestTime) {
                    // Lập kỷ lục mới!
                    localStorage.setItem(`game-best-time-${category}-${subcategory}`, gameTime.toFixed(1));
                    if (recordTimeEl) recordTimeEl.innerText = gameTime.toFixed(1) + 's';
                    if (newRecordBadge) newRecordBadge.style.display = 'inline-block';
                } else {
                    if (recordTimeEl) recordTimeEl.innerText = bestTime.toFixed(1) + 's';
                    if (newRecordBadge) newRecordBadge.style.display = 'none';
                }

                // Ẩn bảng chơi và hiện màn hình chiến thắng
                const gameBoardContainer = document.getElementById('game-board-container');
                const gameVictoryContainer = document.getElementById('game-victory-container');

                if (gameBoardContainer) gameBoardContainer.style.display = 'none';
                if (gameVictoryContainer) gameVictoryContainer.style.display = 'block';

                // Kích nổ pháo hoa ăn mừng
                try {
                    triggerConfetti();
                } catch(e) {}
            }
        } catch (err) {
            console.error("Lỗi kết thúc game: ", err);
            // Phương án dự phòng để luôn hiển thị màn hình chiến thắng
            const gameBoardContainer = document.getElementById('game-board-container');
            const gameVictoryContainer = document.getElementById('game-victory-container');
            if (gameBoardContainer) gameBoardContainer.style.display = 'none';
            if (gameVictoryContainer) gameVictoryContainer.style.display = 'block';
        }
    }

    function initSearchableDropdown(triggerId, dropdownId, searchInputId, optionsId, textId, hiddenInputId, onSelectCallback) {
        const trigger = document.getElementById(triggerId);
        const dropdown = document.getElementById(dropdownId);
        const searchInput = document.getElementById(searchInputId);
        const optionsContainer = document.getElementById(optionsId);
        const textEl = document.getElementById(textId);
        const hiddenInput = document.getElementById(hiddenInputId);
        const container = trigger ? trigger.parentElement : null;

        if (!trigger || !dropdown || !optionsContainer || !hiddenInput) return null;

        // Toggle open/close
        trigger.addEventListener('click', (e) => {
            e.stopPropagation();
            // Close other searchable dropdowns first
            document.querySelectorAll('.searchable-select').forEach(el => {
                if (el !== container) el.classList.remove('active');
            });
            
            container.classList.toggle('active');
            if (container.classList.contains('active')) {
                dropdown.style.display = 'block';
                if (searchInput) {
                    searchInput.value = '';
                    // Trigger search reset
                    searchInput.dispatchEvent(new Event('input'));
                    searchInput.focus();
                }
            } else {
                dropdown.style.display = 'none';
            }
        });

        // Close on click outside
        document.addEventListener('click', (e) => {
            if (container && !container.contains(e.target)) {
                container.classList.remove('active');
                dropdown.style.display = 'none';
            }
        });

        // Search options
        if (searchInput) {
            const removeTones = (str) => {
                str = str.replace(/à|á|ạ|ả|ã|â|ầ|ấ|ậ|ẩ|ẫ|ă|ằ|ắ|ặ|ẳ|ẵ/g,"a"); 
                str = str.replace(/è|é|ẹ|ẻ|ẽ|ê|ề|ế|ệ|ể|ễ/g,"e"); 
                str = str.replace(/ì|í|ị|ỉ|ĩ/g,"i"); 
                str = str.replace(/ò|ó|ọ|ỏ|õ|ô|ồ|ố|ộ|ổ|ỗ|ơ|ờ|ớ|ợ|ở|ỡ/g,"o"); 
                str = str.replace(/ù|ú|ụ|ủ|ũ|ư|ừ|ứ|ự|ử|ữ/g,"u"); 
                str = str.replace(/ỳ|ý|ỵ|ỷ|ỹ/g,"y"); 
                str = str.replace(/đ/g,"d");
                str = str.replace(/À|Á|Ạ|Ả|Ã|Â|Ầ|Ấ|Ậ|Ẩ|Ẫ|Ă|Ằ|Ắ|Ặ|Ẳ|Ẵ/g, "A");
                str = str.replace(/È|É|Ẹ|Ẻ|Ẽ|Ê|Ề|Ế|Ệ|Ể|Ễ/g, "E");
                str = str.replace(/Ì|Í|Ị|Ỉ|Ĩ/g, "I");
                str = str.replace(/Ò|Ó|Ọ|B|Õ|Ô|Ồ|Ố|Ộ|Ổ|Ỗ|Ơ|Ờ|Ớ|Ợ|Ở|Ỡ/g, "O");
                str = str.replace(/Ù|Ý|Ụ|Ủ|Ũ|Ư|Ừ|Ứ|Ự|Ử|Ữ/g, "U");
                str = str.replace(/Ỳ|Ý|Ỵ|Ỷ|Ỹ/g, "Y");
                str = str.replace(/Đ/g, "D");
                str = str.replace(/\u0300|\u0301|\u0303|\u0309|\u0323/g, ""); 
                str = str.replace(/\u02C6|\u0306|\u031B/g, ""); 
                return str;
            };

            searchInput.addEventListener('input', (e) => {
                const query = removeTones(e.target.value.toLowerCase().trim());
                const items = optionsContainer.querySelectorAll('.option-item');
                items.forEach(opt => {
                    const txt = removeTones(opt.innerText.toLowerCase());
                    if (txt.includes(query)) {
                        opt.style.display = 'flex';
                    } else {
                        opt.style.display = 'none';
                    }
                });
            });
        }

        // Return a method to update the options
        return {
            updateOptions: (optionsList) => {
                optionsContainer.innerHTML = '';
                
                // Add "Tất cả chủ đề"
                const allOpt = document.createElement('div');
                allOpt.className = 'option-item active';
                allOpt.style.cssText = 'padding: 0.6rem 1rem; cursor: pointer; color: var(--text-primary); transition: background 0.2s ease; display: flex; align-items: center; justify-content: space-between; font-size: 0.9rem;';
                allOpt.innerHTML = `<span>Tất cả chủ đề</span><i class="fas fa-check check-icon" style="color: var(--primary-color); font-size: 0.8rem;"></i>`;
                
                allOpt.addEventListener('click', () => {
                    optionsContainer.querySelectorAll('.option-item').forEach(el => el.classList.remove('active'));
                    allOpt.classList.add('active');
                    
                    hiddenInput.value = 'all';
                    if (textEl) textEl.innerText = 'Tất cả chủ đề';
                    
                    container.classList.remove('active');
                    dropdown.style.display = 'none';
                    if (onSelectCallback) onSelectCallback('all');
                });
                optionsContainer.appendChild(allOpt);

                // Add other options
                optionsList.forEach(optVal => {
                    const opt = document.createElement('div');
                    opt.className = 'option-item';
                    opt.style.cssText = 'padding: 0.6rem 1rem; cursor: pointer; color: var(--text-primary); transition: background 0.2s ease; display: flex; align-items: center; justify-content: space-between; font-size: 0.9rem;';
                    opt.innerHTML = `<span>${optVal}</span><i class="fas fa-check check-icon" style="display: none; color: var(--primary-color); font-size: 0.8rem;"></i>`;
                    
                    opt.addEventListener('click', () => {
                        optionsContainer.querySelectorAll('.option-item').forEach(el => {
                            el.classList.remove('active');
                            const chk = el.querySelector('.check-icon');
                            if (chk) chk.style.display = 'none';
                        });
                        opt.classList.add('active');
                        const chk = opt.querySelector('.check-icon');
                        if (chk) chk.style.display = 'inline-block';
                        
                        hiddenInput.value = optVal;
                        if (textEl) textEl.innerText = optVal;
                        
                        container.classList.remove('active');
                        dropdown.style.display = 'none';
                        if (onSelectCallback) onSelectCallback(optVal);
                    });
                    optionsContainer.appendChild(opt);
                });

                // Reset selected values
                hiddenInput.value = 'all';
                if (textEl) textEl.innerText = 'Tất cả chủ đề';
            }
        };
    }

    // Launch App!
    function launchApp() {
        checkAndUpdateStreak(false); // Cập nhật chuỗi khi tải trang
        init();
        setupStreakEventListeners(); // Lắng nghe sự kiện chúc mừng & giả lập
        checkMakeupStudyAlert(); // Kiểm tra lịch học bù khi mở ứng dụng
    }
    
    launchApp();
});
