# Tài liệu kiến trúc MindSprint AI (bản ASP.NET Core MVC)

> **Dành cho nhóm môn Công nghệ phần mềm (CNPM).** Tài liệu này giúp nhóm hiểu project mới khi đọc lại các sơ đồ cũ (`MindSprint-AI-D-n/docs/architecture/`). Nó **không thay** tài liệu nộp của nhóm CNPM; nhóm tự quyết có cập nhật sơ đồ của mình hay không. Mọi tên bảng, lớp, đường dẫn dưới đây lấy từ mã nguồn project mới ngày 15/11/2026.

## 1. Thay đổi lớn nhất trong một câu

Bản cũ là **hai phần tách rời** (giao diện HTML/JS gọi API bằng JWT); bản mới là **một ứng dụng ASP.NET Core MVC**: máy chủ dựng sẵn trang (Razor), JS chỉ lo phần tương tác. Chức năng và giao diện giữ theo bản nhóm; khung (database, quy tắc nghiệp vụ, bảo mật) lấy từ project QuizEduManager.

| | Bản cũ (MindSprint-AI-D-n) | Bản mới (MindSprintAI_ASP.NET_MVC) |
|---|---|---|
| Kiến trúc | Frontend tĩnh (Vercel) + Web API .NET 8 (Render) | Một project ASP.NET Core MVC .NET 10 |
| Database | PostgreSQL (Render) / SQL Server, 12 bảng | SQL Server, 49 bảng + bảng tài khoản của Identity |
| Đăng nhập | JWT + refresh token | ASP.NET Identity (cookie) + phiên thiết bị, xác thực 2 bước, passkey, Google |
| Logic | Nhiều logic nằm trong `script.js` (218 KB, chạy trên trình duyệt) | Quy tắc nghiệp vụ nằm trong `Services/` (chạy trên máy chủ) |
| Thời gian thực | Không | SignalR (`Hubs/LearnHub.cs`): thông báo, theo dõi bài làm |
| Vai trò người dùng | Một loại người học | Học viên, Giảng viên, Quản trị viên, Khách vào lớp bằng mã |

## 2. Sơ đồ thành phần (mới, bản cũ không có)

```mermaid
flowchart LR
    Browser["Trình duyệt<br/>Razor View + JS nhỏ theo trang<br/>(flashcards.js, notebook.js, kanban.js…)"]
    subgraph App["ASP.NET Core MVC — một tiến trình"]
        C["Controllers/<br/>nhận yêu cầu, kiểm quyền"]
        S["Services/<br/>quy tắc nghiệp vụ"]
        H["Hubs/LearnHub<br/>SignalR"]
        EF["Data/AppDbContext<br/>EF Core + Migrations"]
    end
    DB[("SQL Server")]
    Gemini["Google Gemini API<br/>(key riêng của từng người)"]
    GoogleId["Google Identity<br/>(đăng nhập Google)"]
    Smtp["Máy chủ thư SMTP<br/>(xác nhận email)"]
    Web["Trang web công khai<br/>(nguồn từ link)"]
    Browser -- "HTTP: trang + form + fetch" --> C
    Browser <-- "WebSocket" --> H
    C --> S --> EF --> DB
    H --> S
    S --> Gemini
    S --> Web
    C --> GoogleId
    S --> Smtp
```

Quy ước làm việc: **Database → Service → Controller → View**. View không tự chấm điểm hay kiểm quyền; mọi kiểm tra nằm ở Service.

## 3. Use case (đối chiếu với `usecase.mmd` cũ)

Giữ nguyên số UC1–UC18 của nhóm; UC19 trở đi là phần thêm từ QuizEdu.

```mermaid
flowchart LR
    Guest["Khách vào lớp bằng mã"]
    Learner["Học viên"]
    Lecturer["Giảng viên"]
    Admin["Quản trị viên"]
    AI["Google Gemini"]
    Google["Google Identity"]
    Web["Nguồn URL công khai"]
    subgraph App["MindSprint AI (MVC)"]
        UC1(["UC1: Ôn thẻ lặp lại ngắt quãng"])
        UC2(["UC2: Tạo thẻ, dán nhiều thẻ, tạo thẻ AI"])
        UC3(["UC3: Thư viện bộ học + thư viện từ vựng"])
        UC4(["UC4: Tổng quan học tập"])
        UC5(["UC5: Luyện trắc nghiệm / tự luận / game"])
        UC6(["UC6: Tạo, mở, xóa sổ tay"])
        UC7(["UC7: Thêm/xóa nguồn chữ, tệp, ảnh, link"])
        UC8(["UC8: Hỏi đáp có trích dẫn + gợi ý"])
        UC9(["UC9: Studio, lưu ghi chú / bộ học"])
        UC10(["UC10: Gia sư, giọng nói"])
        UC11(["UC11: Lịch học + chuông + học bù"])
        UC12(["UC12: Công việc Kanban"])
        UC13(["UC13: Chuỗi ngày học"])
        UC14(["UC14: Đăng ký, đăng nhập, đăng xuất"])
        UC15(["UC15: Đăng nhập Google"])
        UC16(["UC16: Gửi bù đánh giá khi có mạng"])
        UC17(["UC17: Ôn thẻ khi mất mạng"])
        UC18(["UC18: Điểm yếu → tạo thẻ ôn / luyện tập"])
        UC20(["UC20: Tạo lớp, mã vào lớp, duyệt thành viên"])
        UC21(["UC21: Giao bài Ôn tập / Kiểm tra"])
        UC22(["UC22: Làm bài, nộp, xem kết quả"])
        UC23(["UC23: Báo cáo lớp, chấm tay, công bố điểm"])
        UC24(["UC24: Nhập câu hỏi từ Excel"])
        UC25(["UC25: Quản trị tài khoản, nhật ký"])
    end
    Guest --> UC14 & UC22
    Learner --> UC1 & UC2 & UC3 & UC4 & UC5 & UC6 & UC7 & UC8 & UC9
    Learner --> UC10 & UC11 & UC12 & UC13 & UC14 & UC15 & UC16 & UC17 & UC18 & UC22
    Lecturer --> UC2 & UC20 & UC21 & UC23 & UC24
    Admin --> UC25
    UC2 & UC8 & UC9 & UC10 --> AI
    UC15 --> Google
    UC7 --> Web
```

| UC cũ | Khác gì ở bản mới |
|---|---|
| UC14 "làm mới, đăng xuất" | Không còn refresh token; phiên cookie tự gia hạn. Có thêm "Đăng xuất tất cả thiết bị" |
| UC16, UC17 | Chỉ áp cho ôn thẻ; tự học luôn được, bài lớp giao do giảng viên bật ("Cho học viên ôn thẻ tiếp khi mất mạng") |
| UC4, UC13 | Số liệu tính trên máy chủ từ hoạt động thật, trình duyệt không tự khai số thẻ / ngày học |
| UC19 (AI lập kế hoạch, mục tiêu cũ) | Chưa làm, như bản nhóm (GAP-01) |

## 4. ERD (đối chiếu với `erd.mmd` cũ)

Chỉ vẽ các bảng chính; danh sách đủ 49 bảng ở mục 4.2.

```mermaid
erDiagram
    AspNetUsers ||--o{ StudySets : "OwnerId"
    StudySets ||--o{ Quizzes : "StudySetId"
    Quizzes ||--o{ Questions : "QuizId"
    Questions ||--o{ QuestionOptions : "QuestionId"
    StudySets ||--o{ Flashcards : "StudySetId"
    Flashcards ||--o{ FlashcardProgresses : "FlashcardId"
    Flashcards ||--o{ FlashcardReviews : "FlashcardId"
    Classrooms ||--o{ ClassMembers : "ClassroomId"
    AspNetUsers ||--o{ ClassMembers : "UserId"
    Classrooms ||--o{ Assignments : "ClassroomId"
    StudySets ||--o{ Assignments : "StudySetId"
    Assignments ||--o{ Participants : "AssignmentId"
    Participants ||--o{ Attempts : "ParticipantId"
    Attempts ||--o{ AttemptQuestions : "AttemptId"
    AttemptQuestions ||--o{ AnswerSubmissions : "AttemptQuestionId"
    AspNetUsers ||--o{ KanbanTasks : "UserId"
    AspNetUsers ||--o{ StudySlots : "UserId"
    AspNetUsers ||--o{ Notebooks : "UserId"
    Notebooks ||--o{ NotebookSources : "NotebookId"
    Notebooks ||--o{ NotebookNotes : "NotebookId"
    AspNetUsers ||--o{ KnownDevices : "UserId"
    AspNetUsers ||--o| UserApiKeys : "UserId"

    AspNetUsers {
        string Id PK
        string Email
        string DisplayName
        int CurrentMode "Học viên / Giảng viên"
        bool IsGuest
    }
    StudySets {
        int Id PK
        string OwnerId FK
        string Title
        int FlashcardMode
    }
    Flashcards {
        int Id PK
        int StudySetId FK
        string Front
        string Back
    }
    FlashcardProgresses {
        int Id PK
        int FlashcardId FK
        string UserId FK "tự học"
        int ParticipantId FK "bài giao"
        int LastRating
    }
    Assignments {
        int Id PK
        int ClassroomId FK
        int StudySetId FK
        int Mode "Ôn tập / Kiểm tra"
        bool AllowOfflineCards
    }
    KanbanTasks {
        int Id PK
        string UserId FK
        string Title
        int Status
        int Position
    }
    StudySlots {
        int Id PK
        string UserId FK
        int DayOfWeek
        time StartTime
        time EndTime
    }
    Notebooks {
        int Id PK
        string UserId FK
        string Title
    }
    NotebookSources {
        int Id PK
        int NotebookId FK
        int Kind "chữ / PDF / tệp / ảnh / link"
        string Content
    }
```

> **Khác bản cũ:** mọi khóa ngoại của các bảng học tập đặt **Restrict** (không xóa dây chuyền). Xóa sổ tay hay tài khoản thì service tự xóa con trước, trong một giao dịch. Bản cũ dùng CASCADE.

### 4.1 Bảng cũ → bảng mới

| Bảng cũ | Bảng mới | Ghi chú |
|---|---|---|
| Users | AspNetUsers (`ApplicationUser`) | Bảng của ASP.NET Identity |
| RefreshTokens | — (+ KnownDevices) | Không dùng JWT; phiên thiết bị lưu ở KnownDevices |
| Flashcards (thẻ của mỗi người, có danh mục) | StudySets + Flashcards | Thẻ thuộc một bộ học. Thẻ từ vựng hệ thống nằm trong `Data/Vocab/vocab.json`, không ở database |
| CardProgresses | FlashcardProgresses + FlashcardReviews | Thêm lịch sử từng lần đánh giá |
| Tasks | KanbanTasks | |
| Notebooks, NotebookSources, NotebookNotes | giữ tên | |
| NotebookGenerationCaches | — | Không lưu đệm kết quả AI |
| UserStudyDays | — | Tính từ FlashcardReviews + AnswerSubmissions |
| StudySchedules | StudySlots | Bỏ "bật/tắt" (giao diện cũ không dùng) |
| WeakAreas | — | Tính ngay khi mở trang Điểm yếu |

### 4.2 Đủ 49 bảng theo nhóm

- **Học liệu:** StudySets, Quizzes, Questions, QuestionOptions, QuestionBlanks, BlankAnswers, QuestionRevisions, Flashcards, FlashcardProgresses, FlashcardReviews, SourceTexts
- **Lớp và bài giao:** Classrooms, ClassMembers, JoinCodes, JoinCodeFailures, Invitations, ClassTransfers, Assignments, AssignmentChanges, Participants, Guests, RetakeRequests, UnhideRequests
- **Làm bài và chấm:** Attempts, AttemptQuestions, AnswerSubmissions, Regrades, ScoreHistories, DeviceChangeRequests, ScreenLeaves
- **Tài khoản và an toàn:** KnownDevices, AccountLocks, UserApiKeys, UserSettingPresets, AdminAccessLogs, AdminRoleChanges, AuditEntries, ContentReports
- **AI:** AiConversations, AiMessages, AiCitations, Notebooks, NotebookSources, NotebookNotes
- **Tổ chức:** KanbanTasks, StudySlots, Notifications, Reminders, ReminderDigests

## 5. Sơ đồ tuần tự: đăng nhập + ôn thẻ (thay `sequence-auth-flashcard.mmd`)

```mermaid
sequenceDiagram
    autonumber
    actor U as Người học
    participant B as Trình duyệt (Razor + flashcards.js)
    participant A as AccountController
    participant L as LoginService
    participant F as FlashcardsController / FlashcardService
    participant DB as SQL Server
    U->>B: Nhập email, mật khẩu
    B->>A: POST /Account/Login (form + mã chống giả mạo)
    A->>L: LoginAsync
    L->>DB: Kiểm mật khẩu, khóa tài khoản, email đã xác nhận
    alt Sai hoặc bị khóa
        L-->>B: Hiện lại form + lý do
    else Đúng
        L->>DB: Ghi phiên thiết bị (KnownDevices)
        L-->>B: Cookie đăng nhập (có mã phiên thiết bị)
    end
    U->>B: Mở Ôn thẻ, lật thẻ, chọn Đã nhớ
    B->>F: POST /Flashcards/Rate (fetch)
    F->>DB: Kiểm quyền + hạn nộp, lưu FlashcardReview, cập nhật FlashcardProgress
    F-->>B: Số thẻ đã học, số thẻ đến hạn
    opt Mất mạng (tự học, hoặc giảng viên cho phép)
        B->>B: Lưu tạm đánh giá theo tài khoản (offline-cards.js), chuyển thẻ
        B->>F: Có mạng lại → gửi bù đúng thứ tự
    end
```

## 6. Sơ đồ tuần tự: hỏi đáp Sổ tay AI (thay `sequence-notebook.mmd`)

```mermaid
sequenceDiagram
    autonumber
    actor U as Người học
    participant B as Trình duyệt (notebook.js)
    participant N as NotebooksController
    participant S as NotebookService
    participant K as UserApiKeyService
    participant G as GeminiClient
    participant API as Google Gemini
    U->>B: Gõ câu hỏi
    B->>N: POST /Notebooks/AskJson/{id}
    N->>S: AskAsync
    S->>S: Kiểm chủ sổ tay, ghép nguồn, chọn đoạn liên quan (SourceGrounding)
    S->>K: Lấy key Gemini của chính người dùng (đã đồng ý thông báo AI)
    S->>G: AskAsync (chỉ các đoạn đã chọn, hạn 30 giây)
    G->>API: generateContent (khuôn trả lời có trích dẫn)
    API-->>G: Câu trả lời + trích dẫn
    G-->>S: Kết quả
    S->>S: Kiểm trích dẫn có thật trong nguồn
    S-->>B: Câu trả lời + trích dẫn
    B->>B: Hiện, lưu lịch sử trên trình duyệt, kèm nút Nghe và Lưu vào ghi chú
```

## 7. Sơ đồ hoạt động: Kanban (thay `activity-kanban.mmd`)

```mermaid
flowchart TD
    A([Mở Công việc]) --> B[KanbanController.Index: tải việc của chính mình]
    B --> C{Thao tác}
    C -->|Thêm / sửa| D[POST Create / Edit]
    C -->|Kéo sang cột khác| E[POST Move: cột + vị trí]
    C -->|Xóa| F[POST Delete]
    D & E & F --> G{"KanbanService kiểm tra:<br/>chủ sở hữu, tiêu đề ≤ 200,<br/>mô tả ≤ 5.000, vị trí hợp lệ"}
    G -->|Hợp lệ| H[Lưu SQL Server, sắp lại thứ tự]
    G -->|Sai| I[Báo lỗi, thẻ ở nguyên chỗ cũ]
    H --> J([Tải lại bảng cho khớp database])
```

## 8. Sơ đồ hoạt động: Sổ tay AI (thay `activity-notebook.mmd`)

```mermaid
flowchart TD
    A([Mở Sổ tay AI]) --> B{Có sổ tay?}
    B -->|Chưa| C[Tạo sổ tay]
    B -->|Có| D[Thêm nguồn]
    C --> D
    D --> E{Loại nguồn}
    E -->|Chữ dán / tệp PDF, TXT| F[Đọc chữ, cắt ≤ 200.000 ký tự]
    E -->|Ảnh đề| G[Gemini đọc chữ trong ảnh]
    E -->|Link| H[Chỉ địa chỉ công khai, ≤ 2 MB chữ]
    F & G & H --> I[(Lưu NotebookSources, tối đa 20 nguồn)]
    I --> J{Học với nguồn}
    J -->|Hỏi đáp| K[Câu trả lời có trích dẫn]
    J -->|Studio| L[Tóm tắt, hướng dẫn ôn, FAQ, dòng thời gian,<br/>sơ đồ tư duy, nghe tổng quan, thẻ, trắc nghiệm]
    J -->|Gia sư| M[Giải bài, kiểm tra bài làm, gợi ý, xem code]
    K & L & M --> N{Lưu?}
    N -->|Ghi chú| O[(NotebookNotes)]
    N -->|Thẻ / trắc nghiệm| P[Trang xem trước → bấm Lưu mới tạo bộ học]
```

## 9. Từ ngữ hay gặp

| Từ | Nghĩa |
|---|---|
| MVC | Mô hình Model – View – Controller: Controller nhận yêu cầu, gọi Service, trả về View (trang HTML) |
| Razor View | Tệp `.cshtml`: HTML có chèn C#, máy chủ dựng thành trang |
| Service | Lớp chứa quy tắc nghiệp vụ (ví dụ `KanbanService`), không phụ thuộc giao diện |
| Migration | Tệp mô tả thay đổi database (EF Core); dự án chỉ **thêm** bảng/cột, không xóa |
| Identity cookie | Cách ASP.NET ghi nhớ đăng nhập bằng cookie, thay cho JWT của bản cũ |
| SignalR | Kênh hai chiều để máy chủ đẩy thông báo tức thời tới trình duyệt |
| Phiên thiết bị | Mỗi lần đăng nhập trên một máy được ghi lại; có thể đăng xuất từ xa |
