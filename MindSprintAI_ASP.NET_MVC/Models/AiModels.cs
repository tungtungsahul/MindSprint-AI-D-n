namespace MindSprintAI_ASP.NET_MVC.Models;

public record SourceEditModel(int StudySetId, string Title, string Content, int Version);
public record SourceReadModel(int AssignmentId, int StudySetId, string Title, string Content, int Version);
public record AiQuestionChoice(int Id, string Label);
public record AiPanelModel(int AssignmentId, long? AttemptId = null, List<AiQuestionChoice>? Questions = null);
public record AiQuestionStat(int? QuestionId, string Label, string Count);
