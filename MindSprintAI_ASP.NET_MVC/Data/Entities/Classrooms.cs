namespace MindSprintAI_ASP.NET_MVC.Data.Entities;

public class Classroom
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public ClassJoinPolicy JoinPolicy { get; set; } = ClassJoinPolicy.Immediate;
    public bool LeaderboardEnabled { get; set; } = true;   // bảng xếp hạng cho học viên (người dùng chốt 17/11: GV tắt được, mặc định bật)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    public List<ClassMember> Members { get; set; } = [];
}

public class ClassMember
{
    public int Id { get; set; }
    public int ClassroomId { get; set; }
    public Classroom Classroom { get; set; } = null!;
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    public ClassRole Role { get; set; }
    public ClassMemberStatus Status { get; set; }

    // Chọn GV phụ được thêm sớm nhất khi nâng lên GV chính (B5c).
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public DateTime? JoinedAt { get; set; }
    public DateTime? LeftAt { get; set; }
    public string? InvitedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Mọi mã 8 số của lớp và bài giao; dòng không bao giờ bị xóa nên mã không tái sử dụng (39, 44).
public class JoinCode
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public JoinCodeTarget TargetType { get; set; }
    public int TargetId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? ReplacedAt { get; set; }
    public int? ReplacedByCodeId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
