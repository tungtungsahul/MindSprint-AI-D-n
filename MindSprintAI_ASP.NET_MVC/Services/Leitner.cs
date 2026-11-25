using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record LeitnerState(int Box, DateOnly? DueDate, bool WasDue);

public static class Leitner
{
    // (D2a) ngày lịch VN, khoảng cách theo hộp mới; thẻ chưa ôn luôn đến hạn.
    public static bool IsDue(int? box, DateOnly? due, DateOnly today) => box == null || due == null || today >= due;

    public static LeitnerState Review(int? box, DateOnly? due, FlashcardRating rating, DateOnly today)
    {
        if (!Enum.IsDefined(rating)) throw new BusinessException("Đánh giá không hợp lệ.");
        if (!IsDue(box, due, today)) return new(box ?? 1, due, false);
        var next = rating == FlashcardRating.NotRemembered ? 1 : Math.Min(5, (box ?? 1) + 1);
        return new(next, today.AddDays(1 << (next - 1)), true);
    }
}
