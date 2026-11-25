namespace MindSprintAI_ASP.NET_MVC.Data.Entities;

// Giá trị số cố định (ERD mục 0): chỉ thêm giá trị mới vào cuối, không đổi số cũ.

public enum UserMode { Learner = 1, Lecturer = 2 }

public enum ClassJoinPolicy { Immediate = 1, RequireApproval = 2 }

public enum ClassRole { MainLecturer = 1, AssistantLecturer = 2, Student = 3 }

public enum ClassMemberStatus { Invited = 1, PendingApproval = 2, Active = 3, Left = 4, Removed = 5, InvitationDeclined = 6, InvitationRevoked = 7, InvitationExpired = 8 }

public enum JoinCodeTarget { Classroom = 1, Assignment = 2 }

public enum QuizStatus { Draft = 1, Ready = 2 }

public enum QuestionType
{
    SingleChoice = 1,
    MultipleChoice = 2,
    TextInput = 3,
    NumberInput = 4,
    SurveySingle = 11,
    SurveyMultiple = 12,
    SurveyText = 13
}

public enum BlankScoring { AllOrNothing = 1, PerBlank = 2 }

public enum AssignmentMode { Practice = 1, Test = 2 }

public enum AssignmentForm { QuizOnly = 1, FlashcardOnly = 2, Combined = 3, Document = 4 }   // Document: bài làm theo đề tài liệu (GV tải đề, chấm tay)

public enum RequiredParts { Quiz = 1, Flashcards = 2, Both = 3 }

public enum ScoreSelection { Highest = 1, First = 2, Latest = 3, Average = 4 }

public enum ScoreScale { Ten = 1, Original = 2, Hundred = 3 }

public enum RoundingMode { TwoDecimals = 1, OneDecimal = 2, Quarter = 3, Half = 4 }

public enum AudiencePolicy { IncludeLateJoiners = 1, OnlyCurrentMembers = 2 }

public enum StrangerPolicy { Reject = 1, RequireApproval = 2, Allow = 3 }

public enum AssignmentStatus { Draft = 1, Open = 2, Closed = 3 }

public enum CloseReason { DueDate = 1, LecturerClosedNow = 2, LecturerClosedDelayed = 3, HiddenByAdmin = 4 }

public enum ApprovalStatus { NotRequired = 1, Pending = 2, Approved = 3, Rejected = 4 }

public enum CompletionStatus
{
    NotStarted = 1,
    InProgress = 2,
    Paused = 3,
    SubmittedAwaitingScore = 4,
    Completed = 5,
    Incomplete = 6
}

[Flags]
public enum CompletionLabels
{
    None = 0,
    Expired = 1,
    ClosedByLecturer = 2,
    LeftClass = 4,
    StoppedByLock = 8,
    RemovedFromClass = 16,
    MissingRequiredSurvey = 32,
    CompletedBeforeCardsRequired = 64,
    RetakeNotUsed = 128,
    MadeAsGuest = 256,
    HiddenByAdmin = 512
}

public enum AttemptKind { Normal = 1, Retake = 2, SelfReview = 3 }

public enum AttemptStatus { InProgress = 1, Paused = 2, Submitted = 3, Ended = 4, Cancelled = 5 }

public enum AttemptEndReason
{
    Submitted = 1,
    DueDate = 2,
    ClosedByLecturer = 3,
    AccountLocked = 4,
    RemovedFromClass = 5,
    FinishedAfterMastery = 6,
    HiddenByAdmin = 7,
    ScreenLeaveLimit = 8 // (H8)
}

public enum AttemptQuestionState { Unanswered = 1, Answered = 2, LockedTimeout = 3, ResetByEdit = 4 }

public enum RetakeRequestStatus { Pending = 1, Approved = 2, Rejected = 3, Used = 4, Expired = 5 }
