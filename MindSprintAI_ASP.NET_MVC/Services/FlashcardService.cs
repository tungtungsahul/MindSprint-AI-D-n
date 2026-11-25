using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record CardItem(int Id, string Front, string Back, bool IsNew, bool Edited, FlashcardRating? Rating,
    int Box = 1, DateOnly? DueDate = null, bool Due = true, string? Example = null);
public record CardSession(int StudySetId, int? AssignmentId, string Title, List<CardItem> Cards,
    int StartIndex, int Studied, int Required, CompletionStatus? Status, bool CanStudy, bool ReviewForgotten, bool AfterDue = false,
    FlashcardMode Mode = FlashcardMode.Traditional, bool CanChooseMode = false, int DueCount = 0, DateOnly? NextDueDate = null, bool DueOnly = false,
    bool OfflineAllowed = false); // ôn tiếp khi mất mạng: tự học luôn được; bài giao chỉ khi GV bật AllowOfflineCards
public record ForgottenCard(string Front, int Count);
public record CardCounts(int Studied, int Total, int? DueCount = null, DateOnly? NextDueDate = null)
{
    public string ProgressLabel => $"Thẻ: {Studied}/{Total} đã học";
    public string MissingLabel => $"Thiếu thẻ ({Studied}/{Total})";
}

public static class CardCompletion
{
    public static bool HasCards(Assignment a) => a.Mode == AssignmentMode.Practice && a.Form != AssignmentForm.QuizOnly;
    public static bool RequiresCards(Assignment a) => HasCards(a) && (a.Form == AssignmentForm.FlashcardOnly || a.RequiredParts is RequiredParts.Flashcards or RequiredParts.Both);
    public static bool RequiresQuiz(Assignment a) => a.Form != AssignmentForm.FlashcardOnly && (a.Form == AssignmentForm.QuizOnly || a.RequiredParts is RequiredParts.Quiz or RequiredParts.Both);
    public static bool Ended(Assignment a, DateTime now) => a.Status == AssignmentStatus.Closed || a.DueAt <= now;

    public static async Task<CardCounts> CountsAsync(AppDbContext db, Assignment a, int? participantId, bool beforeEnd = false)
    {
        var cards = db.Flashcards.Where(c => c.StudySetId == a.StudySetId && c.DeletedAt == null && c.CreatedAt <= a.CreatedAt);
        var cutoff = a.ClosedAt is { } closed && (a.DueAt == null || closed < a.DueAt) ? closed : a.DueAt;
        var studied = participantId == null ? 0 : await cards.CountAsync(c => db.FlashcardProgresses.Any(p =>
            p.ParticipantId == participantId && p.FlashcardId == c.Id && (!beforeEnd || cutoff == null || p.FirstReviewedAt <= cutoff)));
        return new CardCounts(studied, await cards.CountAsync());
    }

    public static async Task RefreshAsync(AppDbContext db, Participant p, DateTime now)
    {
        var a = p.Assignment;
        if (!HasCards(a) || p.CardsCompletionFinalizedAt != null) return;
        var ended = Ended(a, now);
        if (p.CompletionStatus != CompletionStatus.Completed)
        {
            var counts = await CountsAsync(db, a, p.Id, beforeEnd: ended);
            var quizDone = p.Attempts.Any(x => x.Status == AttemptStatus.Ended && x.Kind != AttemptKind.SelfReview);
            var done = (!RequiresQuiz(a) || quizDone) && (!RequiresCards(a) || counts.Studied == counts.Total);
            p.CompletionStatus = done ? CompletionStatus.Completed
                : ended && RequiresCards(a) ? CompletionStatus.Incomplete
                // Đã nộp quiz nhưng còn thiếu thẻ bắt buộc vẫn là "Đang làm", không phải "Chưa làm" (69, B5a').
                : p.Attempts.Any(x => x.Kind != AttemptKind.SelfReview && AttemptRules.IsOpen(x)) || quizDone || counts.Studied > 0 ? CompletionStatus.InProgress : CompletionStatus.NotStarted;
        }
        if (ended) p.CardsCompletionFinalizedAt = now; // (B5a') từ đây chỉ tiến độ học tiếp được thay đổi.
    }
}

public partial class FlashcardService(AppDbContext db, StudyLibraryService library, TimeProvider clock, ILearnNotifier notifier)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // Bài trước đây chặn học thẻ sau hạn: dùng tiến độ cá nhân, không ghi tiến độ bắt buộc.
    private async Task<bool> SelfReviewCardsAsync(Assignment? a, string userId)
    {
        if (a == null || !a.AllowCardSelfReview || !CardCompletion.Ended(a, Now) || a.AllowFlashcardsAfterDue) return false;
        var member = await db.ClassMembers.FirstOrDefaultAsync(m => m.ClassroomId == a.ClassroomId
            && m.UserId == userId && m.Role == ClassRole.Student && m.Status == ClassMemberStatus.Active
            && m.Classroom.DeletedAt == null && !m.User.IsGuest);
        if (a.Mode != AssignmentMode.Practice || member == null || !AssignmentService.IsAssignedTo(a, member)) return false;
        var p = await db.Participants.SingleOrDefaultAsync(p => p.AssignmentId == a.Id && p.UserId == userId);
        return p?.IsStranger != true && !AssignmentService.IsOpen(a, Now, p);
    }

    public async Task<(StudySet Set, List<Flashcard> Cards)> GetEditorAsync(int studySetId, string userId)
    {
        var set = await library.GetOwnedStudySetAsync(studySetId, userId);
        return (set, await db.Flashcards.Where(c => c.StudySetId == set.Id && c.DeletedAt == null).OrderBy(c => c.OrderIndex).ToListAsync());
    }

    public const int MaxExample = 2000;
    public async Task<int> SaveAsync(int studySetId, int? cardId, string? front, string? back, string userId, string? example = null)
    {
        await library.GetOwnedStudySetAsync(studySetId, userId);
        if (string.IsNullOrWhiteSpace(front) || string.IsNullOrWhiteSpace(back)) throw new BusinessException("Nhập đủ mặt trước và mặt sau.");
        example = string.IsNullOrWhiteSpace(example) ? null : example.Trim();
        if (example?.Length > MaxExample) throw new BusinessException($"Ví dụ minh họa tối đa {MaxExample} ký tự.");
        var card = cardId == null ? new Flashcard { StudySetId = studySetId, CreatedAt = Now,
            OrderIndex = (await db.Flashcards.Where(c => c.StudySetId == studySetId).MaxAsync(c => (int?)c.OrderIndex) ?? -1) + 1 }
            : await db.Flashcards.SingleOrDefaultAsync(c => c.Id == cardId && c.StudySetId == studySetId && c.DeletedAt == null)
                ?? throw new ForbiddenException();
        if (cardId == null) db.Flashcards.Add(card);
        else card.EditedAt = Now; // (B5d) giữ nguyên mọi đánh giá khi sửa thẻ.
        card.Front = front.Trim();
        card.Back = back.Trim();
        card.Example = example;
        db.Audit?.Add(cardId == null ? AuditAction.FlashcardCreated : AuditAction.FlashcardEdited, "Flashcard", card.Id, userId, resolveTarget: () => card.Id.ToString()); // (NK3)
        await db.SaveChangesAsync();
        return card.Id;
    }

    // "Dán nhiều thẻ" ở trang Soạn thẻ (nhập hàng loạt của nhóm, PasteCards): cùng quy tắc kiểm tra như nhập Excel
    // (ExcelTemplate.ValidateRows); có dòng sai thì không thêm thẻ nào. Thêm cả loạt trong một giao dịch.
    public async Task<int> AddManyAsync(int studySetId, string? text, string userId)
    {
        await library.GetOwnedStudySetAsync(studySetId, userId);
        var rows = ExcelTemplate.ValidateRows(PasteCards.Rows(text));
        var bad = rows.Where(r => r.Error != null).ToList();
        if (bad.Count > 0)
            throw new BusinessException($"Có {bad.Count} dòng chưa đúng, chưa thêm thẻ nào. " +
                // Lỗi của ValidateRows đánh số theo dòng Excel ("Dòng 3: …") → đổi sang số dòng đã dán (bắt đầu từ 1).
                string.Join("; ", bad.Take(3).Select(r => $"Dòng {r.Row - ExcelTemplate.FirstDataRow + 1}: {r.Error![(r.Error!.IndexOf(": ", StringComparison.Ordinal) + 2)..]}"))
                + (bad.Count > 3 ? "; …" : ""));
        await using var tx = await db.Database.BeginTransactionAsync();
        var order = (await db.Flashcards.Where(c => c.StudySetId == studySetId).MaxAsync(c => (int?)c.OrderIndex) ?? -1) + 1;
        var cards = rows.Select((r, i) => new Flashcard { StudySetId = studySetId, CreatedAt = Now, OrderIndex = order + i, Front = r.Card!.Front.Trim(), Back = r.Card.Back.Trim() }).ToList();
        db.Flashcards.AddRange(cards);
        foreach (var card in cards) db.Audit?.Add(AuditAction.FlashcardCreated, "Flashcard", card.Id, userId, resolveTarget: () => card.Id.ToString()); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return cards.Count;
    }

    public async Task DeleteAsync(int studySetId, int cardId, string userId)
    {
        await library.GetOwnedStudySetAsync(studySetId, userId);
        await using var tx = await db.Database.BeginTransactionAsync();
        var assignmentIds = await db.Assignments.Where(a => a.StudySetId == studySetId && a.Form != AssignmentForm.QuizOnly)
            .OrderBy(a => a.Id).Select(a => a.Id).ToListAsync();
        foreach (var id in assignmentIds) await AttemptLock.AssignmentAsync(db, id);
        var card = await db.Flashcards.SingleOrDefaultAsync(c => c.Id == cardId && c.StudySetId == studySetId && c.DeletedAt == null)
            ?? throw new ForbiddenException();
        // (B5d) chốt bài đã hết hạn trước khi mẫu số thay đổi.
        var participants = await db.Participants.Include(p => p.Assignment).Include(p => p.Attempts)
            .Where(p => assignmentIds.Contains(p.AssignmentId)).OrderBy(p => p.Id).ToListAsync();
        foreach (var p in participants)
        {
            await AttemptLock.ParticipantAsync(db, p.Id);
            if (CardCompletion.Ended(p.Assignment, Now))
            {
                foreach (var attempt in p.Attempts.Where(AttemptRules.IsOpen).ToList())
                {
                    var current = await AttemptLock.LoadAsync(db, attempt.Id);
                    AttemptRules.ExpireIfDue(current, p, Now);
                }
                await CardCompletion.RefreshAsync(db, p, Now);
            }
        }
        card.DeletedAt = Now;
        await db.SaveChangesAsync();
        foreach (var p in participants) await CardCompletion.RefreshAsync(db, p, Now);
        db.Audit?.Add(AuditAction.FlashcardDeleted, "Flashcard", cardId, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        foreach (var id in assignmentIds) await notifier.LecturersAsync(id);
    }

    private async Task<(Assignment Assignment, Participant Participant)> LearnerAsync(int assignmentId, string userId)
    {
        var a = await AttemptLock.LearnerAsync(db, assignmentId, userId);
        var member = await AttemptLock.MemberForLearnerAsync(db, a.ClassroomId, userId);
        var selfReview = await SelfReviewCardsAsync(a, userId);
        if (!selfReview) await AssignmentAccess.EnsureStartAsync(db, a, member, userId);
        var hiddenNotice = ContentVisibility.LearnerNotice(await ContentVisibility.HiddenReasonAsync(db, a.StudySetId));
        if (hiddenNotice != null) throw new BusinessException(hiddenNotice);
        if (!CardCompletion.HasCards(a)) throw new BusinessException("Bài này không có thẻ ghi nhớ.");
        if (!selfReview && a.OpensAt > Now) throw new BusinessException("Bài chưa mở.");
        var p = await db.Participants.Include(p => p.Attempts).SingleOrDefaultAsync(p => p.AssignmentId == a.Id && p.UserId == userId);
        if (p == null)
        {
            p = new Participant { Assignment = a, UserId = userId, CreatedAt = Now };
            db.Participants.Add(p);
            await db.SaveChangesAsync();
        }
        await AttemptLock.ParticipantAsync(db, p.Id);
        if (selfReview) return (a, p);
        foreach (var attempt in p.Attempts.Where(AttemptRules.IsOpen).ToList())
        {
            var current = await AttemptLock.LoadAsync(db, attempt.Id);
            AttemptRules.ExpireIfDue(current, p, Now);
        }
        await CardCompletion.RefreshAsync(db, p, Now);
        await db.SaveChangesAsync();
        return (a, p);
    }

    public async Task<CardSession> GetSessionAsync(int studySetId, int? assignmentId, string userId, bool forgotten = false,
        bool dueOnly = false, FlashcardMode? mode = null)
    {
        await EnsureNotTakingTestAsync(studySetId, userId);
        await using var tx = await db.Database.BeginTransactionAsync();
        Assignment? a = null;
        Participant? p = null;
        StudySet set;
        if (assignmentId != null)
        {
            (a, p) = await LearnerAsync(assignmentId.Value, userId);
            if (a.StudySetId != studySetId) throw new ForbiddenException();
            set = await db.StudySets.SingleAsync(s => s.Id == studySetId);
        }
        else
        {
            if (await db.Users.AnyAsync(u => u.Id == userId && u.IsGuest)) throw new ForbiddenException();
            set = await library.GetOwnedStudySetAsync(studySetId, userId);
        }
        var selfReview = await SelfReviewCardsAsync(a, userId);
        var canStudy = selfReview || a == null || !CardCompletion.Ended(a, Now) || a.AllowFlashcardsAfterDue;
        var activeMode = ResolveMode(a, p, set, mode);
        if (dueOnly && activeMode != FlashcardMode.SpacedRepetition) throw new BusinessException("Chỉ ôn thẻ đến hạn trong chế độ Ôn ngắt quãng.");
        var participantId = selfReview ? null : p?.Id;
        var progress = await db.FlashcardProgresses.Where(x => x.ParticipantId == participantId && x.UserId == userId && x.Flashcard.StudySetId == studySetId).ToListAsync();
        var cards = await db.Flashcards.Where(c => c.StudySetId == studySetId && c.DeletedAt == null).OrderBy(c => c.OrderIndex).ToListAsync();
        var today = VnTime.Date(Now);
        var items = canStudy ? cards.Select(c =>
        {
            var state = progress.SingleOrDefault(x => x.FlashcardId == c.Id);
            return new CardItem(c.Id, c.Front, c.Back, a != null && c.CreatedAt > a.CreatedAt,
                c.EditedAt != null, TraditionalRating(state), state?.SrsBox ?? 1, state?.SrsDueDate,
                Leitner.IsDue(state?.SrsBox, state?.SrsDueDate, today), c.Example);
        }).ToList() : [];
        var dueCount = items.Count(c => c.Due);
        var nextDue = items.Where(c => !c.Due).Select(c => c.DueDate).Min();
        if (forgotten) items = items.Where(c => c.Rating == FlashcardRating.NotRemembered).ToList();
        if (dueOnly) items = items.Where(c => c.Due).ToList();
        var counts = a == null || selfReview ? new CardCounts(cards.Count(c => progress.Any(x => x.FlashcardId == c.Id)), cards.Count)
            : await CardCompletion.CountsAsync(db, a, p!.Id);
        if (p?.GuestId != null && canStudy) { p.GuestStartedAt ??= Now; await db.SaveChangesAsync(); }
        await tx.CommitAsync();
        if (a != null) await notifier.LecturersAsync(a.Id);
        return new CardSession(set.Id, a?.Id, set.Title, items, activeMode == FlashcardMode.Traditional ? Math.Max(0, items.FindIndex(c => c.Rating == null)) : 0,
            counts.Studied, counts.Total, p?.CompletionStatus, canStudy, forgotten, a != null && CardCompletion.Ended(a, Now),
            activeMode, a == null || a.FlashcardMode == FlashcardMode.LearnerChoice, dueCount, nextDue, dueOnly, a == null || a.AllowOfflineCards);
    }

    // Trả về bộ đếm mới để trang học cập nhật ngay, không phải tải lại.
    public async Task<(CardCounts Counts, CompletionStatus? Status)> RateAsync(int studySetId, int? assignmentId, int cardId,
        FlashcardRating rating, string userId, FlashcardMode? mode = null)
    {
        await EnsureNotTakingTestAsync(studySetId, userId);
        if (!Enum.IsDefined(rating)) throw new BusinessException("Đánh giá không hợp lệ.");
        await using var tx = await db.Database.BeginTransactionAsync();
        Participant? p = null;
        Assignment? a = null;
        StudySet? set = null;
        if (assignmentId != null)
        {
            (a, var participant) = await LearnerAsync(assignmentId.Value, userId);
            p = participant;
            if (a.StudySetId != studySetId) throw new ForbiddenException();
        }
        else
        {
            if (await db.Users.AnyAsync(u => u.Id == userId && u.IsGuest)) throw new ForbiddenException();
            set = await library.GetOwnedStudySetAsync(studySetId, userId);
            // Tự ôn cũng tuần tự hóa cập nhật để không mất lần đánh giá khi hai tab gửi cùng lúc.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [StudySets] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {studySetId}");
            await AttemptLock.PersonalCardsAsync(db, studySetId, userId);
            await db.Entry(set).ReloadAsync();
        }
        var selfReview = await SelfReviewCardsAsync(a, userId);
        if (a != null && !selfReview && CardCompletion.Ended(a, Now) && !a.AllowFlashcardsAfterDue)
            throw new BusinessException("Bài không cho học thẻ sau hạn.");
        if (selfReview)
        {
            // Cùng khóa với học cá nhân và các bài khác dùng chung tiến độ cá nhân (theo người, không khóa dòng bộ học — 13/11/2026).
            await AttemptLock.PersonalCardsAsync(db, studySetId, userId);
        }
        var activeMode = ResolveMode(a, p, set, mode);
        if (!await db.Flashcards.AnyAsync(c => c.Id == cardId && c.StudySetId == studySetId && c.DeletedAt == null)) throw new ForbiddenException();
        var participantId = selfReview ? null : p?.Id;
        var progress = await db.FlashcardProgresses.SingleOrDefaultAsync(x => x.FlashcardId == cardId && x.UserId == userId
            && x.ParticipantId == participantId);
        if (progress == null)
        {
            progress = new FlashcardProgress { ParticipantId = participantId, UserId = userId, FlashcardId = cardId, FirstReviewedAt = Now,
                HasTraditionalRating = false };
            db.FlashcardProgresses.Add(progress);
        }
        else await db.Entry(progress).ReloadAsync();
        var history = new FlashcardReview { StudySetId = studySetId, FlashcardId = cardId, ParticipantId = participantId,
            UserId = userId, Mode = activeMode, Rating = rating, BeforeRating = TraditionalRating(progress),
            BeforeBox = progress.SrsBox ?? 1, BeforeDueDate = progress.SrsDueDate, ReviewedAt = Now,
            WasDue = Leitner.IsDue(progress.SrsBox, progress.SrsDueDate, VnTime.Date(Now)) };
        // (D3a) giữ mốc cũ trước khi LastReviewedAt chung nhận một lần ôn SRS đến hạn.
        if (progress.HasTraditionalRating != false && (activeMode == FlashcardMode.Traditional || history.WasDue))
            progress.TraditionalReviewedAt ??= progress.LastReviewedAt;
        if (activeMode == FlashcardMode.Traditional)
        {
            progress.LastRating = rating;
            progress.HasTraditionalRating = true;
            progress.TraditionalReviewedAt = Now;
        }
        else
        {
            var next = Leitner.Review(progress.SrsBox, progress.SrsDueDate, rating, VnTime.Date(Now));
            if (next.WasDue) { progress.SrsBox = next.Box; progress.SrsDueDate = next.DueDate; }
        }
        history.AfterRating = TraditionalRating(progress);
        history.AfterBox = progress.SrsBox ?? 1; history.AfterDueDate = progress.SrsDueDate;
        db.FlashcardReviews.Add(history); // (D3b) cùng transaction và khóa với tiến độ.
        if (activeMode == FlashcardMode.Traditional || history.WasDue)
        {
            progress.LastReviewedAt = Now;
            progress.ReviewCount++;
        }
        if (p?.GuestId != null) p.GuestStartedAt ??= Now;
        await db.SaveChangesAsync();
        if (p != null && !selfReview) await CardCompletion.RefreshAsync(db, p, Now);
        await db.SaveChangesAsync();
        var counts = a != null && !selfReview ? await CardCompletion.CountsAsync(db, a, p!.Id)
            : new CardCounts(await db.FlashcardProgresses.CountAsync(x => x.ParticipantId == null && x.UserId == userId
                    && x.Flashcard.StudySetId == studySetId && x.Flashcard.DeletedAt == null),
                await db.Flashcards.CountAsync(c => c.StudySetId == studySetId && c.DeletedAt == null));
        if (activeMode == FlashcardMode.SpacedRepetition)
        {
            var today = VnTime.Date(Now);
            var cards = await db.Flashcards.Where(c => c.StudySetId == studySetId && c.DeletedAt == null).Select(c => c.Id).ToListAsync();
            var states = await db.FlashcardProgresses.Where(x => cards.Contains(x.FlashcardId) && x.UserId == userId && x.ParticipantId == participantId).ToListAsync();
            counts = counts with { DueCount = cards.Count(id => { var s = states.SingleOrDefault(x => x.FlashcardId == id); return Leitner.IsDue(s?.SrsBox, s?.SrsDueDate, today); }),
                NextDueDate = states.Where(s => !Leitner.IsDue(s.SrsBox, s.SrsDueDate, today)).Select(s => s.SrsDueDate).Min() };
        }
        await tx.CommitAsync();
        if (a != null) await notifier.LecturersAsync(a.Id);
        return (counts, p?.CompletionStatus);
    }

    private Task EnsureNotTakingTestAsync(int studySetId, string userId) =>
        StudySetLearningAccess.EnsureNotTakingTestAsync(db, studySetId, userId);
}
