namespace MindSprintAI_ASP.NET_MVC.Models;

public record JoinQrModel(string Code, string JoinUrl, string JoinPageUrl);

// Popup mã QR ngay trên trang (lớp, bài giao); trang JoinQr riêng chỉ còn để in/trình chiếu.
public record QrPopupModel(string Id, string Title, JoinQrModel Join, string PrintUrl);
