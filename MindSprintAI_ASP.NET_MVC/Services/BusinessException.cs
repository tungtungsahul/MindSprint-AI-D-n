namespace MindSprintAI_ASP.NET_MVC.Services;

// Vi phạm quy tắc nghiệp vụ; Message là câu tiếng Việt hiện thẳng cho người dùng.
public class BusinessException(string message) : Exception(message);

// Không có quyền với dữ liệu này (controller trả 403).
public class ForbiddenException() : Exception("Bạn không có quyền thực hiện thao tác này.");
