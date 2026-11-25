namespace MindSprintAI_ASP.NET_MVC.Data.Entities;

public class SourceText : IVersioned
{
    public int StudySetId { get; set; }
    public StudySet StudySet { get; set; } = null!;
    public string Content { get; set; } = "";
    public int Version { get; set; } = 1;
}

public class UserApiKey
{
    public string UserId { get; set; } = "";
    public string EncryptedKey { get; set; } = "";
    public string LastFour { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    // Pháp lý: lúc người dùng đồng ý thông báo "gửi sang Google Gemini ở nước ngoài"; rỗng thì trợ lý không gửi gì.
    public DateTime? AiNoticeAcceptedAt { get; set; }
}

public class AiConversation
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public int StudySetId { get; set; }
    public int? AssignmentId { get; set; }
    public int? QuestionId { get; set; }
    public string Title { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<AiMessage> Messages { get; set; } = [];
}

public class AiMessage
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public int SourceVersion { get; set; }
    // Lưu theo từng lượt hỏi để đổi câu trong cùng hội thoại vẫn thống kê đúng.
    public int? QuestionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<AiCitation> Citations { get; set; } = [];
}

public class AiCitation
{
    public int Id { get; set; }
    public int AiMessageId { get; set; }
    public string ExcerptText { get; set; } = "";
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
}
