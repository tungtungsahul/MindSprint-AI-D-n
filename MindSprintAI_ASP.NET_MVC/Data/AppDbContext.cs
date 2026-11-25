using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    // (NK3) Gắn theo scope HTTP; context dựng trực tiếp trong test vẫn có thể gắn AuditLog.
    public Services.AuditLog? Audit { get; set; }
    public DbSet<DeviceChangeRequest> DeviceChangeRequests => Set<DeviceChangeRequest>();
    public DbSet<ScreenLeave> ScreenLeaves => Set<ScreenLeave>();
    public DbSet<SourceText> SourceTexts => Set<SourceText>();
    public DbSet<UserApiKey> UserApiKeys => Set<UserApiKey>();
    public DbSet<AiConversation> AiConversations => Set<AiConversation>();
    public DbSet<AiMessage> AiMessages => Set<AiMessage>();
    public DbSet<AiCitation> AiCitations => Set<AiCitation>();
    public DbSet<UserSettingPreset> UserSettingPresets => Set<UserSettingPreset>();
    public DbSet<Classroom> Classrooms => Set<Classroom>();
    public DbSet<ClassMember> ClassMembers => Set<ClassMember>();
    public DbSet<JoinCode> JoinCodes => Set<JoinCode>();
    public DbSet<StudySet> StudySets => Set<StudySet>();
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<QuestionOption> QuestionOptions => Set<QuestionOption>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<AssignmentChange> AssignmentChanges => Set<AssignmentChange>();
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<Guest> Guests => Set<Guest>();
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<AttemptQuestion> AttemptQuestions => Set<AttemptQuestion>();
    public DbSet<AnswerSubmission> AnswerSubmissions => Set<AnswerSubmission>();
    public DbSet<PaperSubmission> PaperSubmissions => Set<PaperSubmission>();
    public DbSet<PaperFile> PaperFiles => Set<PaperFile>();
    public DbSet<RetakeRequest> RetakeRequests => Set<RetakeRequest>();
    public DbSet<QuestionBlank> QuestionBlanks => Set<QuestionBlank>();
    public DbSet<BlankAnswer> BlankAnswers => Set<BlankAnswer>();
    public DbSet<Flashcard> Flashcards => Set<Flashcard>();
    public DbSet<FlashcardProgress> FlashcardProgresses => Set<FlashcardProgress>();
    public DbSet<FlashcardReview> FlashcardReviews => Set<FlashcardReview>();
    public DbSet<QuestionRevision> QuestionRevisions => Set<QuestionRevision>();
    public DbSet<Regrade> Regrades => Set<Regrade>();
    public DbSet<ScoreHistory> ScoreHistories => Set<ScoreHistory>();
    public DbSet<AccountLock> AccountLocks => Set<AccountLock>();
    public DbSet<JoinCodeFailure> JoinCodeFailures => Set<JoinCodeFailure>();
    public DbSet<ContentReport> ContentReports => Set<ContentReport>();
    public DbSet<UnhideRequest> UnhideRequests => Set<UnhideRequest>();
    public DbSet<AdminAccessLog> AdminAccessLogs => Set<AdminAccessLog>();
    public DbSet<AdminRoleChange> AdminRoleChanges => Set<AdminRoleChange>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ClassTransfer> ClassTransfers => Set<ClassTransfer>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<ReminderDigest> ReminderDigests => Set<ReminderDigest>();
    public DbSet<KnownDevice> KnownDevices => Set<KnownDevice>();
    public DbSet<KanbanTask> KanbanTasks => Set<KanbanTask>();
    public DbSet<StudySlot> StudySlots => Set<StudySlot>();
    public DbSet<Notebook> Notebooks => Set<Notebook>();
    public DbSet<NotebookSource> NotebookSources => Set<NotebookSource>();
    public DbSet<NotebookNote> NotebookNotes => Set<NotebookNote>();

    // SQL Server trả datetime2 không kèm múi giờ; đánh dấu lại là UTC để khi gửi ra trình duyệt
    // (ToString("O") có chữ Z) không bị hiểu nhầm thành giờ máy, lệch 7 tiếng.
    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        c.Properties<DateTime>().HaveConversion<UtcConverter>();
        c.Properties<DateTime?>().HaveConversion<UtcConverter>();
    }

    private class UtcConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
        v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<ApplicationUser>().Property(x => x.AvatarUrl).HasMaxLength(500);
        b.Entity<ApplicationUser>().Property(x => x.AvatarSource).HasConversion<int>().HasDefaultValue(AvatarSource.None);
        b.Entity<ApplicationUser>().Property(x => x.AvatarHidden).HasDefaultValue(false);
        b.Entity<AuditEntry>(e =>
        {
            e.Property(x => x.ActorUserId).HasMaxLength(450);
            e.Property(x => x.ActorGuestId).HasMaxLength(450);
            e.Property(x => x.TargetType).HasMaxLength(50);
            e.Property(x => x.TargetId).HasMaxLength(64);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.RequestId).HasMaxLength(100);
            e.HasIndex(x => x.At);
            e.HasIndex(x => new { x.ActorUserId, x.At });
            e.HasIndex(x => new { x.Action, x.At });
            e.HasIndex(x => x.RequestId);
        });
        b.Entity<DeviceChangeRequest>(e =>
        {
            e.Property(x => x.RequestingDeviceId).HasMaxLength(64);
            e.Property(x => x.Reason).HasMaxLength(200);
            e.Property(x => x.DecidedById).HasMaxLength(450);
            e.HasIndex(x => x.AttemptId).IsUnique().HasFilter("[Status] = 1"); // (F2)
        });
        b.Entity<ScreenLeave>().HasIndex(x => new { x.AttemptId, x.StartedAt });
        b.Entity<ScreenLeave>().HasIndex(x => new { x.AttemptId, x.Kind }).IsUnique().HasFilter("[EndedAt] IS NULL");
        foreach (var type in new[] { typeof(KnownDevice), typeof(Attempt), typeof(DeviceChangeRequest) })
        {
            b.Entity(type).Property<string>("LightHash").HasMaxLength(64);
            b.Entity(type).Property<string>("NetworkPrefix").HasMaxLength(48);
        }
        b.Entity<Attempt>().Property(x => x.FullHash).HasMaxLength(64);
        b.Entity<DeviceChangeRequest>().Property(x => x.FullHash).HasMaxLength(64);
        b.Entity<Assignment>().Property(a => a.FlashcardMode).HasDefaultValue(FlashcardMode.Traditional);
        b.Entity<StudySet>().Property(a => a.FlashcardMode).HasDefaultValue(FlashcardMode.Traditional);
        b.Entity<Participant>().Property(a => a.FlashcardMode).HasDefaultValue(FlashcardMode.Traditional);
        b.Entity<Assignment>().Property(a => a.CriticalThreshold).HasPrecision(18, 4);
        b.Entity<Assignment>().Property(a => a.CriticalQuestionIdsJson).HasDefaultValue("[]");
        b.Entity<FlashcardReview>(e =>
        {
            e.Property(x => x.UserId).HasMaxLength(450);
            e.HasIndex(x => new { x.UserId, x.StudySetId, x.ReviewedAt });
            e.HasIndex(x => new { x.ParticipantId, x.StudySetId, x.ReviewedAt });
        });
        b.Entity<ApplicationUser>().Property(x => x.EmailRemindersEnabled).HasDefaultValue(true);
        b.Entity<ApplicationUser>().Property(x => x.SrsBellEnabled).HasDefaultValue(true); // (D5c) cả tài khoản hiện có.
        b.Entity<ApplicationUser>().Property(x => x.DeviceChangeBellEnabled).HasDefaultValue(false); // (F11) cả dòng cũ.
        b.Entity<ApplicationUser>().Property(x => x.ScreenLeaveBellEnabled).HasDefaultValue(false); // (F11)
        b.Entity<ClassTransfer>().Property(x => x.InvitedEmail).HasMaxLength(256);
        b.Entity<Invitation>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.NormalizedEmail).HasMaxLength(256);
            e.Property(x => x.InviterId).HasMaxLength(450);
            e.Property(x => x.RecipientId).HasMaxLength(450);
            e.Property(x => x.TransferFromUserId).HasMaxLength(450);
            e.HasIndex(x => new { x.NormalizedEmail, x.Status, x.ExpiresAt });
            e.HasIndex(x => new { x.InviterId, x.CreatedAt });
            e.HasIndex(x => new { x.ClassroomId, x.Kind, x.Status });
            e.HasOne(x => x.Classroom).WithMany().HasForeignKey(x => x.ClassroomId);
        });
        b.Entity<Reminder>(e =>
        {
            e.Property(x => x.UserId).HasMaxLength(450);
            e.Property(x => x.Text).HasMaxLength(1000);
            e.Property(x => x.Url).HasMaxLength(300);
            e.HasIndex(x => new { x.UserId, x.Kind, x.TargetId }).IsUnique();
            e.HasIndex(x => new { x.EmailedAt, x.Id });
        });
        b.Entity<ReminderDigest>(e =>
        {
            e.Property(x => x.UserId).HasMaxLength(450);
            e.HasIndex(x => new { x.UserId, x.VietnamDate }).IsUnique();
        });
        b.Entity<Notebook>(e =>
        {
            e.Property(x => x.UserId).HasMaxLength(450);
            e.Property(x => x.Title).HasMaxLength(200);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId);
            e.HasIndex(x => x.UserId);
            e.HasMany(x => x.Sources).WithOne(x => x.Notebook).HasForeignKey(x => x.NotebookId); // quy ước cuối file: không xóa dây chuyền → xóa nguồn/ghi chú trước
            e.HasMany(x => x.Notes).WithOne(x => x.Notebook).HasForeignKey(x => x.NotebookId);
        });
        b.Entity<NotebookSource>(e => { e.Property(x => x.Title).HasMaxLength(200); e.Property(x => x.Origin).HasMaxLength(260); });
        b.Entity<NotebookNote>(e => e.Property(x => x.Title).HasMaxLength(200));
        b.Entity<StudySlot>(e =>
        {
            e.Property(x => x.UserId).HasMaxLength(450);
            e.Property(x => x.Note).HasMaxLength(40);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId);
            e.HasIndex(x => new { x.UserId, x.DayOfWeek, x.StartTime });
        });
        b.Entity<KanbanTask>(e =>
        {
            e.Property(x => x.UserId).HasMaxLength(450);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(5000);
            e.Property(x => x.Priority).HasDefaultValue(KanbanPriority.Medium);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId);
            e.HasIndex(x => new { x.UserId, x.Status, x.Position }); // đọc bảng theo cột + thứ tự
        });
        b.Entity<KnownDevice>(e =>
        {
            e.Property(x => x.UserId).HasMaxLength(450);
            e.Property(x => x.DeviceHash).HasMaxLength(64);
            e.Property(x => x.Label).HasMaxLength(100);
            e.Property(x => x.LastSignInIp).HasMaxLength(45);
            e.Property(x => x.LastLocation).HasMaxLength(200);
            e.HasIndex(x => new { x.UserId, x.DeviceHash }).IsUnique();
        });
        // V3 mặc định rút ngắn khóa/PhoneNumber của V1; giữ nguyên cột cũ để migration chỉ thêm bảng.
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<string>>().Property(x => x.LoginProvider).Metadata.SetMaxLength(null);
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<string>>().Property(x => x.ProviderKey).Metadata.SetMaxLength(null);
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<string>>().Property(x => x.LoginProvider).Metadata.SetMaxLength(null);
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<string>>().Property(x => x.Name).Metadata.SetMaxLength(null);
        b.Entity<ApplicationUser>().Property(x => x.PhoneNumber).Metadata.SetMaxLength(null);

        b.Entity<SourceText>(e =>
        {
            e.HasKey(x => x.StudySetId);
            e.Property(x => x.Content).HasMaxLength(50000);
            e.HasOne(x => x.StudySet).WithOne().HasForeignKey<SourceText>(x => x.StudySetId);
        });
        b.Entity<UserApiKey>(e =>
        {
            e.HasKey(x => x.UserId);
            e.Property(x => x.LastFour).HasMaxLength(4);
            e.HasOne<ApplicationUser>().WithOne().HasForeignKey<UserApiKey>(x => x.UserId);
        });
        b.Entity<AiConversation>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(120);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId);
            e.HasOne<StudySet>().WithMany().HasForeignKey(x => x.StudySetId);
            e.HasOne<Assignment>().WithMany().HasForeignKey(x => x.AssignmentId);
            e.HasOne<Question>().WithMany().HasForeignKey(x => x.QuestionId);
            e.HasIndex(x => new { x.UserId, x.DeletedAt });
        });
        b.Entity<AiMessage>(e =>
        {
            e.Property(x => x.Role).HasMaxLength(12);
            e.HasOne<AiConversation>().WithMany(x => x.Messages).HasForeignKey(x => x.ConversationId);
            e.HasOne<Question>().WithMany().HasForeignKey(x => x.QuestionId);
        });
        b.Entity<AiCitation>().HasOne<AiMessage>().WithMany(x => x.Citations).HasForeignKey(x => x.AiMessageId);

        b.Entity<UserSettingPreset>(e =>
        {
            e.HasKey(p => new { p.UserId, p.Scope });
            e.Property(p => p.Scope).HasMaxLength(80);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.UserId);
        });

        b.Entity<ApplicationUser>().Property(u => u.DisplayName).HasMaxLength(100);
        b.Entity<Guest>(e =>
        {
            e.Property(g => g.DisplayName).HasMaxLength(60);
            e.Property(g => g.BrowserTokenHash).HasMaxLength(64).IsUnicode(false).IsFixedLength();
            e.HasIndex(g => g.BrowserTokenHash).IsUnique();
            e.HasIndex(g => g.UserId).IsUnique();
        });
        b.Entity<StudySet>().Property(s => s.HiddenReason).HasMaxLength(500);
        b.Entity<AccountLock>(e =>
        {
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasIndex(x => new { x.UserId, x.UnlockedAt });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.LockedById);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UnlockedById);
        });
        b.Entity<JoinCodeFailure>(e =>
        {
            e.Property(x => x.KeyHash).HasMaxLength(64).IsUnicode(false).IsFixedLength();
            e.HasIndex(x => new { x.KeyType, x.KeyHash }).IsUnique();
        });
        b.Entity<ContentReport>(e =>
        {
            e.Property(x => x.TargetId).HasMaxLength(128);
            e.Property(x => x.Reason).HasMaxLength(100);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasIndex(x => new { x.ReporterId, x.TargetType, x.TargetId }).IsUnique()
                .HasFilter("[Status] = 1");
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReporterId);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.TargetOwnerId);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.HandledById);
            e.HasOne<StudySet>().WithMany().HasForeignKey(x => x.StudySetId);
        });
        b.Entity<UnhideRequest>(e =>
        {
            e.Property(x => x.Message).HasMaxLength(1000).IsRequired();
            e.Property(x => x.DecisionNote).HasMaxLength(500);
            e.HasIndex(x => x.StudySetId).IsUnique().HasFilter("[Status] = 1");
            e.HasOne(x => x.StudySet).WithMany().HasForeignKey(x => x.StudySetId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.DecidedById).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<AdminAccessLog>(e =>
        {
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AdminId);
            e.HasOne<ContentReport>().WithMany().HasForeignKey(x => x.ContentReportId);
        });
        b.Entity<AdminRoleChange>(e =>
        {
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AdminId);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.TargetUserId);
        });

        b.Entity<Classroom>().Property(c => c.Name).HasMaxLength(200);
        b.Entity<Notification>(e =>
        {
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId);
            e.HasIndex(x => new { x.UserId, x.ReadAt, x.Id });
        });
        b.Entity<ClassTransfer>(e =>
        {
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.FromUserId);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ToUserId);
            e.HasIndex(x => x.ClassroomId).IsUnique().HasFilter("[Status] = 1")
                .HasDatabaseName("IX_ClassTransfers_OnePendingPerClass");
        });

        b.Entity<ClassMember>(e =>
        {
            e.HasIndex(m => new { m.ClassroomId, m.UserId }).IsUnique();
            // Mỗi lớp đúng một GV chính đang hoạt động (48A).
            e.HasIndex(m => m.ClassroomId).IsUnique()
                .HasFilter($"[Role] = {(int)ClassRole.MainLecturer} AND [Status] = {(int)ClassMemberStatus.Active}")
                .HasDatabaseName("IX_ClassMembers_OneMainLecturer");
        });

        b.Entity<JoinCode>(e =>
        {
            e.Property(c => c.Code).HasMaxLength(8).IsFixedLength();
            e.HasIndex(c => c.Code).IsUnique();
            e.HasIndex(c => new { c.TargetType, c.TargetId }).IsUnique()
                .HasFilter("[IsActive] = 1")
                .HasDatabaseName("IX_JoinCodes_OneActivePerTarget");
        });

        b.Entity<StudySet>().Property(s => s.Title).HasMaxLength(200);
        b.Entity<StudySet>().Property(s => s.Category).HasMaxLength(Services.StudyLibraryService.MaxCategory);
        b.Entity<Flashcard>().Property(c => c.Example).HasMaxLength(Services.FlashcardService.MaxExample);
        b.Entity<Quiz>().Property(q => q.Title).HasMaxLength(200);

        // Bài làm theo đề tài liệu.
        b.Entity<Assignment>(e =>
        {
            e.Property(a => a.PaperTitle).HasMaxLength(200);
            e.Property(a => a.PaperFileName).HasMaxLength(255);
            e.Property(a => a.PaperStoredName).HasMaxLength(64);
        });
        b.Entity<PaperSubmission>(e =>
        {
            e.HasKey(x => x.AttemptId);
            e.HasOne(x => x.Attempt).WithOne().HasForeignKey<PaperSubmission>(x => x.AttemptId);
            e.Property(x => x.Score).HasPrecision(4, 2);
            e.Property(x => x.Comment).HasMaxLength(Services.PaperTestService.MaxComment);
            e.Property(x => x.GradedById).HasMaxLength(450);
        });
        b.Entity<PaperFile>(e =>
        {
            e.HasOne(x => x.Submission).WithMany(s => s.Files).HasForeignKey(x => x.AttemptId);
            e.Property(x => x.FileName).HasMaxLength(255);
            e.Property(x => x.ContentType).HasMaxLength(100);
            e.Property(x => x.StoredName).HasMaxLength(64);
            e.HasIndex(x => x.AttemptId);
        });

        b.Entity<Question>(e =>
        {
            e.HasQueryFilter(q => q.DeletedAt == null);
            e.Property(q => q.Points).HasPrecision(9, 4);
            e.Property(q => q.NumberAnswer).HasPrecision(18, 6);
            e.Property(q => q.NumberTolerance).HasPrecision(18, 6);
            // (5, 65C) chuyển decimal qua chuỗi invariant để giữ mọi chữ số mà không ALTER cột cũ.
            e.Property(q => q.NumberAnswer).HasField("_numberAnswer").UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Property(q => q.NumberTolerance).HasField("_numberTolerance").UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Property(q => q.NumberAnswerExact).HasConversion<string>().HasMaxLength(50);
            e.Property(q => q.NumberToleranceExact).HasConversion<string>().HasMaxLength(50);
        });

        b.Entity<Assignment>().Property(a => a.PassMark).HasPrecision(9, 4);
        // (19) mọi đường chấm/kết thúc lượt đều dùng các lần chấm lại của bài giao.
        b.Entity<Assignment>().Navigation(a => a.Regrades).AutoInclude();
        b.Entity<QuestionRevision>(e =>
        {
            e.HasQueryFilter(r => r.Question.DeletedAt == null);
            e.HasIndex(r => new { r.QuestionId, r.Version }).IsUnique();
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.EditedById);
        });
        b.Entity<Regrade>(e =>
        {
            e.HasIndex(r => new { r.AssignmentId, r.ClientRequestId }).IsUnique();
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.ById);
        });
        b.Entity<ScoreHistory>(e =>
        {
            e.HasIndex(h => new { h.RegradeId, h.AttemptId }).IsUnique();
            // (65C) giữ nguyên điểm lẻ trước/sau, không làm tròn lịch sử.
            foreach (var name in new[] { nameof(ScoreHistory.OldFirstScore), nameof(ScoreHistory.NewFirstScore),
                nameof(ScoreHistory.OldPracticeScore), nameof(ScoreHistory.NewPracticeScore),
                nameof(ScoreHistory.OldMaxScore), nameof(ScoreHistory.NewMaxScore) })
                e.Property(name).HasConversion<string>().HasMaxLength(50);
        });
        b.Entity<QuestionBlank>().HasIndex(x => new { x.QuestionId, x.BlankIndex }).IsUnique();
        b.Entity<BlankAnswer>().HasIndex(x => new { x.QuestionBlankId, x.OrderIndex }).IsUnique();
        b.Entity<FlashcardProgress>(e =>
        {
            e.HasIndex(x => new { x.ParticipantId, x.FlashcardId }).IsUnique().HasFilter("[ParticipantId] IS NOT NULL");
            e.HasIndex(x => new { x.UserId, x.FlashcardId }).IsUnique().HasFilter("[ParticipantId] IS NULL AND [UserId] IS NOT NULL");
        });
        b.Entity<AssignmentChange>().Property(c => c.Field).HasMaxLength(100);

        b.Entity<Participant>(e =>
        {
            e.Property(p => p.GuestDisplayName).HasMaxLength(60);
            e.HasIndex(p => new { p.AssignmentId, p.UserId }).IsUnique().HasFilter("[UserId] IS NOT NULL");
            e.HasIndex(p => new { p.AssignmentId, p.GuestId }).IsUnique().HasFilter("[GuestId] IS NOT NULL");
        });

        b.Entity<RetakeRequest>(e =>
        {
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.DecidedById);
            // Chỉ một yêu cầu chờ hoặc khung chưa dùng cho mỗi học viên (68).
            e.HasIndex(r => r.ParticipantId).IsUnique()
                .HasFilter($"[Status] IN ({(int)RetakeRequestStatus.Pending}, {(int)RetakeRequestStatus.Approved})")
                .HasDatabaseName("IX_RetakeRequests_OnePendingPerParticipant");
        });

        b.Entity<Attempt>(e =>
        {
            // (C2, 70) số thứ tự không được trùng dù hai yêu cầu cùng bắt đầu.
            e.HasIndex(a => new { a.ParticipantId, a.AttemptNumber }).IsUnique();
            e.Property(a => a.FirstScore).HasPrecision(9, 4);
            e.Property(a => a.FirstScore).HasField("_firstScore").UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Property(a => a.FirstScoreExact).HasConversion<string>().HasMaxLength(50);
            e.Property(a => a.PracticeScore).HasPrecision(9, 4);
            e.Property(a => a.MaxScore).HasPrecision(9, 4);
            e.Property(a => a.ActiveDeviceId).HasMaxLength(64);
            // Mỗi người chỉ một lượt dở mỗi bài giao, kể cả khi hai máy bấm Bắt đầu cùng lúc (B4a).
            e.HasIndex(a => a.ParticipantId).IsUnique()
                .HasFilter($"[Status] IN ({(int)AttemptStatus.InProgress}, {(int)AttemptStatus.Paused})")
                .HasDatabaseName("IX_Attempts_OneOpenPerParticipant");
        });

        b.Entity<AttemptQuestion>(e =>
        {
            e.Property(q => q.FirstScore).HasPrecision(9, 4);
            e.Property(q => q.LatestScore).HasPrecision(9, 4);
            e.Property(q => q.FirstScore).HasField("_firstScore").UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Property(q => q.LatestScore).HasField("_latestScore").UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Property(q => q.FirstScoreExact).HasConversion<string>().HasMaxLength(50);
            e.Property(q => q.LatestScoreExact).HasConversion<string>().HasMaxLength(50);
            e.Property(q => q.OptionOrderJson).HasMaxLength(400);
        });

        b.Entity<AnswerSubmission>(e =>
        {
            e.Property(s => s.Score).HasPrecision(9, 4);
            e.Property(s => s.Score).HasField("_score").UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Property(s => s.ScoreExact).HasConversion<string>().HasMaxLength(50);
            e.HasIndex(s => s.ClientRequestId).IsUnique();
        });

        foreach (var entity in b.Model.GetEntityTypes())
        {
            if (typeof(IVersioned).IsAssignableFrom(entity.ClrType))
                b.Entity(entity.ClrType).Property(nameof(IVersioned.Version)).IsConcurrencyToken();

            // Không xóa cứng dữ liệu học tập (ERD mục 0), nên không để xóa dây chuyền; bảng Identity giữ mặc định.
            if (entity.ClrType.Namespace == typeof(Classroom).Namespace)
                foreach (var fk in entity.GetForeignKeys())
                    fk.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await Services.AttemptRules.FinalizeEndedAsync(this, cancellationToken);
        Audit?.CaptureRestores(); // (NK3) Điểm chốt chung cho các thay đổi khôi phục.
        foreach (var entry in ChangeTracker.Entries<IVersioned>())
            if (entry.State == EntityState.Modified)
                entry.Entity.Version++;
        // (NK3) Ghi nghiệp vụ trước để có Id thật, nhật ký sau trong CÙNG giao dịch.
        // Tách một SaveChanges để lỗi bảng nhật ký không làm hỏng thay đổi chính.
        var audits = ChangeTracker.Entries<AuditEntry>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
        if (audits.Count == 0) return await base.SaveChangesAsync(cancellationToken);
        foreach (var row in audits) Entry(row).State = EntityState.Detached;
        await using var owned = Database.CurrentTransaction == null ? await Database.BeginTransactionAsync(cancellationToken) : null;
        var count = await base.SaveChangesAsync(cancellationToken);
        try
        {
            foreach (var row in audits)
            {
                if (row.ResolveTarget != null) row.TargetId = row.ResolveTarget();
                AuditEntries.Add(row);
            }
            // (NK3) Kết nối đã tắt MARS ở factory/Program để savepoint được hỗ trợ.
            await Database.CurrentTransaction!.CreateSavepointAsync("BeforeAudit", cancellationToken);
            try { count += await base.SaveChangesAsync(cancellationToken); }
            catch
            {
                await Database.CurrentTransaction.RollbackToSavepointAsync("BeforeAudit", cancellationToken);
                throw;
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            foreach (var row in audits) Entry(row).State = EntityState.Detached;
            if (Audit != null) Audit.Warn();
            else Serilog.Log.Warning("Audit write failed; business operation continues");
        }
        if (owned != null) await owned.CommitAsync(cancellationToken);
        return count;
    }
}
