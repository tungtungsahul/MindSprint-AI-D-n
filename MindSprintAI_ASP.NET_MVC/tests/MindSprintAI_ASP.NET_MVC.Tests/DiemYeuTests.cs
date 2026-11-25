using MindSprintAI_ASP.NET_MVC.Services;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Điểm yếu chuyển từ MindSprint-AI-D-n WeakAreaService: ≥ 3 lần thử và chính xác < 70%.
[Collection("db")]
public class DiemYeuTests
{
    [Fact]
    public void QuyTacNhomVaThuTuYeuNhatTruoc()
    {
        var topics = new[] {
            new WeakArea(WeakKind.Cards, "Ít lần thử", 2, 2, 1, null),      // 0% nhưng chỉ 2 lần → chưa đủ dữ liệu
            new WeakArea(WeakKind.Cards, "Đạt 70%", 3, 10, 2, null),        // 70% → không yếu
            new WeakArea(WeakKind.Quiz, "Yếu vừa", 4, 10, null, 7),         // 60%
            new WeakArea(WeakKind.Quiz, "Yếu nhất", 3, 3, null, 8) };       // 0%
        Assert.Equal(["Yếu nhất", "Yếu vừa"], WeakAreaService.Pick(topics).Select(t => t.Topic));
        Assert.Equal("4/10 lần sai · 60% chính xác", topics[2].Reason);
    }

    [Fact]
    public async Task BaiKiemTraChuaCongBoDapAnKhongLoQuaDiemYeu()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(3);
        var at = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        foreach (var q in at.Questions)
        {
            var wrong = QuestionSnapshot.FromJson(q.SnapshotJson).Options.First(o => !o.IsCorrect).Id;
            await h.Attempts.AnswerAsync(at.Id, q.Id, Guid.NewGuid(), [wrong], s.Student, questionVersion: q.QuestionVersion);
        }
        s.Assignment.Mode = AssignmentMode.Test; // như trang làm bài: bài kiểm tra chỉ lộ đúng/sai sau khi công bố đáp án
        await h.Db.SaveChangesAsync();
        var weak = new WeakAreaService(h.Db);
        Assert.Empty((await weak.GetAsync(s.Student)).Areas);
        await Assert.ThrowsAsync<BusinessException>(() => weak.CreateReviewSetAsync(s.Student, h.Library, h.Clock));
        s.Assignment.AnswersPublishedAt = h.Clock.GetUtcNow().UtcDateTime; // công bố nhưng lượt vẫn đang làm → vẫn giấu
        await h.Db.SaveChangesAsync();
        Assert.Empty((await weak.GetAsync(s.Student)).Areas);
        await h.Attempts.FinishAsync(at.Id, s.Student);
        Assert.Single((await weak.GetAsync(s.Student)).Areas);
    }

    [Fact]
    public async Task TraLoiSaiQuizThanhDiemYeuCuaChinhNguoiDo()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(3);
        var at = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        foreach (var q in at.Questions)
        {
            var wrong = QuestionSnapshot.FromJson(q.SnapshotJson).Options.First(o => !o.IsCorrect).Id;
            await h.Attempts.AnswerAsync(at.Id, q.Id, Guid.NewGuid(), [wrong], s.Student, questionVersion: q.QuestionVersion);
        }
        var page = await new WeakAreaService(h.Db).GetAsync(s.Student);
        var area = Assert.Single(page.Areas);
        Assert.Equal((WeakKind.Quiz, 3, 3, s.Assignment.Id), (area.Kind, area.Wrong, area.Total, area.AssignmentId));
        Assert.Empty((await new WeakAreaService(h.Db).GetAsync(s.Lecturer)).Areas);
        // Tổng quan lấy đúng chủ đề yếu nhất làm "Chủ đề cần cải thiện"; chưa ôn thẻ thì điểm thành thạo 0.
        var overview = await new OverviewService(h.Db, new StudyStatsService(h.Db, h.Clock), new WeakAreaService(h.Db)).GetAsync(s.Student);
        Assert.Equal((0, 0, 3), (overview.MasteryScore, overview.CardsStudied, overview.Today.Answered));
        Assert.Equal(area, overview.Improve);
        Assert.Equal(0, new MasteryRow(1, "Bộ", 0, 0).Percent);
        Assert.Equal(66, new MasteryRow(1, "Bộ", 2, 3).Percent);

        // "Tạo thẻ ôn tập từ điểm yếu": mỗi câu sai thành một thẻ (mặt trước = câu hỏi, mặt sau có đáp án đúng), bộ mới của chính học viên.
        var set = await new WeakAreaService(h.Db).CreateReviewSetAsync(s.Student, h.Library, h.Clock);
        Assert.Equal((s.Student, FlashcardMode.SpacedRepetition), (set.OwnerId, set.FlashcardMode));
        var cards = h.Db.Flashcards.Where(f => f.StudySetId == set.Id).OrderBy(f => f.OrderIndex).ToList();
        Assert.Equal(3, cards.Count);
        foreach (var q in at.Questions.Select(q => QuestionSnapshot.FromJson(q.SnapshotJson)))
            Assert.Contains(cards, c => c.Front == q.Prompt.Trim() && c.Back.Contains(q.Options.First(o => o.IsCorrect).Text));
        // Giảng viên chưa làm sai câu nào → báo lỗi dễ hiểu, không tạo bộ rỗng.
        var none = await Assert.ThrowsAsync<BusinessException>(() => new WeakAreaService(h.Db).CreateReviewSetAsync(s.Lecturer, h.Library, h.Clock));
        Assert.Contains("Chưa có thẻ", none.Message);
    }
}
