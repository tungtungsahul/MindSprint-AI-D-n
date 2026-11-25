using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;
using MindSprintAI_ASP.NET_MVC.Models;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Ôn thẻ khi mất mạng (FR-21/22 của nhóm; người dùng chốt 15/11): tự học luôn được; bài giao mặc định giữ
// "lưu xong mới chuyển thẻ" (QuizEdu D2c/73A), GV tự bật AllowOfflineCards cho bài Ôn tập có thẻ.
[Collection("db")]
public class OnTheKhiMatMangTests
{
    private static async Task<(Scenario S, FlashcardService Cards, Assignment Cardsy)> SetupAsync(Harness h, bool allowOffline)
    {
        var s = await h.ScenarioAsync(1);
        var cards = new FlashcardService(h.Db, h.Library, h.Clock, h.Notifier);
        await cards.SaveAsync(s.Quiz.StudySetId, null, "Mặt trước", "Mặt sau", s.Lecturer);
        var a = await h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.StudySetId,
            new(null, h.Clock.UtcNow.AddDays(2), 3, Form: AssignmentForm.FlashcardOnly, StudySetId: s.Quiz.StudySetId, AllowOfflineCards: allowOffline), s.Lecturer);
        return (s, cards, a);
    }

    [Fact]
    public async Task MacDinhTat_TuHocLuonDuoc_GVBatThiBaiGiaoDuoc()
    {
        using var h = new Harness();
        var (s, cards, a) = await SetupAsync(h, allowOffline: false);
        Assert.False(new Assignment().AllowOfflineCards);
        Assert.False(new AssignmentInput().AllowOfflineCards);
        Assert.False(a.AllowOfflineCards);
        Assert.False((await cards.GetSessionAsync(s.Quiz.StudySetId, a.Id, s.Student)).OfflineAllowed);
        Assert.True((await cards.GetSessionAsync(s.Quiz.StudySetId, null, s.Lecturer)).OfflineAllowed); // tự học bộ của mình

        await h.Assignments.ChangeOfflineCardsAsync(a.Id, true, s.Lecturer);
        Assert.True((await cards.GetSessionAsync(s.Quiz.StudySetId, a.Id, s.Student)).OfflineAllowed);
        var change = await h.Db.AssignmentChanges.SingleAsync(c => c.AssignmentId == a.Id && c.Field == nameof(Assignment.AllowOfflineCards));
        Assert.Equal(("False", "True", s.Lecturer), (change.OldValue, change.NewValue, change.ChangedById));
        // Mẫu cài đặt lần sau nhớ lựa chọn của GV.
        Assert.True((await new UserSettingPresetService(h.Db, h.Clock).AssignmentAsync(AssignmentMode.Practice, s.Lecturer)).AllowOfflineCards);
    }

    [Fact]
    public void GiaoDienGV_OChonNamTrongKhungThe_TrangBaiCoNutDoi()
    {
        var create = File.ReadAllText(Path.Combine(Tang8PureTests.Root, "Views/Assignments/Create.cshtml"));
        // Nằm trong khung "Thẻ ghi nhớ" (tự ẩn khi bài không có thẻ); ô ẩn "false" để bỏ tích vẫn gửi đúng.
        Assert.Matches("id=\"flashcard-mode-field\">[\\s\\S]*?name=\"AllowOfflineCards\" value=\"true\"[\\s\\S]*?name=\"AllowOfflineCards\" value=\"false\"", create);
        var details = File.ReadAllText(Path.Combine(Tang8PureTests.Root, "Views/Assignments/Details.cshtml"));
        Assert.Matches("a.Mode == AssignmentMode.Practice && a.Form != AssignmentForm.QuizOnly\\)\\s*\\{\\s*<form[^>]*asp-action=\"ChangeOfflineCards\"", details);
        var study = File.ReadAllText(Path.Combine(Tang8PureTests.Root, "Views/Flashcards/Study.cshtml"));
        Assert.Contains("data-offline=\"@(Model.OfflineAllowed ? \"true\" : \"false\")\"", study);
    }

    [Fact]
    public async Task ChiBaiOnTapCoThe()
    {
        using var h = new Harness();
        var (s, _, a) = await SetupAsync(h, allowOffline: true);
        Assert.True(a.AllowOfflineCards);
        // Bài chỉ có quiz: không có thẻ để ôn → không bật được, kể cả khi gửi lên true.
        var quizOnly = await h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.Id, new(null, h.Clock.UtcNow.AddDays(2), 3, AllowOfflineCards: true), s.Lecturer);
        Assert.False(quizOnly.AllowOfflineCards);
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeOfflineCardsAsync(quizOnly.Id, true, s.Lecturer));
        // Học viên không đổi được cài đặt của GV.
        await Assert.ThrowsAnyAsync<Exception>(() => h.Assignments.ChangeOfflineCardsAsync(a.Id, false, s.Student));
        Assert.False(SettingPresetRules.Assignment("{\"AllowOfflineCards\":true}", AssignmentMode.Test).AllowOfflineCards);
    }
}
