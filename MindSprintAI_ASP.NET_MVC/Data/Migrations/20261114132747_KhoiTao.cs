using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindSprintAI_ASP.NET_MVC.Data.Migrations
{
    /// <inheritdoc />
    public partial class KhoiTao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AvatarUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AvatarSource = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    AvatarHidden = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AvatarUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CurrentMode = table.Column<int>(type: "int", nullable: false),
                    IsSystemAdmin = table.Column<bool>(type: "bit", nullable: false),
                    IsGuest = table.Column<bool>(type: "bit", nullable: false),
                    EmailRemindersEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SrsBellEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DeviceChangeBellEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ScreenLeaveBellEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PolicyAcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PolicyVersion = table.Column<int>(type: "int", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ActorGuestId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ActorKind = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TargetId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Result = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Classrooms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    JoinPolicy = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Classrooms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JoinCodeFailures",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KeyType = table.Column<int>(type: "int", nullable: false),
                    KeyHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    FailCount = table.Column<int>(type: "int", nullable: false),
                    FirstFailedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastFailedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JoinCodeFailures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JoinCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nchar(8)", fixedLength: true, maxLength: 8, nullable: false),
                    TargetType = table.Column<int>(type: "int", nullable: false),
                    TargetId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ReplacedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReplacedByCodeId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JoinCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KnownDevices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LightHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    NetworkPrefix = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    DeviceHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSignInIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    LastLocation = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnownDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReminderDigests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    VietnamDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReminderDigests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Reminders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    TargetId = table.Column<long>(type: "bigint", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EmailedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reminders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccountLocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LockedById = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    StartsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UnlockedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UnlockedById = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountLocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountLocks_AspNetUsers_LockedById",
                        column: x => x.LockedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountLocks_AspNetUsers_UnlockedById",
                        column: x => x.UnlockedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountLocks_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdminRoleChanges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AdminId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TargetUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Granted = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminRoleChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminRoleChanges_AspNetUsers_AdminId",
                        column: x => x.AdminId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdminRoleChanges_AspNetUsers_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserPasskeys",
                columns: table => new
                {
                    CredentialId = table.Column<byte[]>(type: "varbinary(1024)", maxLength: 1024, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Data = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserPasskeys", x => x.CredentialId);
                    table.ForeignKey(
                        name: "FK_AspNetUserPasskeys_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Guests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    BrowserTokenHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    MergedIntoUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Guests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Guests_AspNetUsers_MergedIntoUserId",
                        column: x => x.MergedIntoUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Guests_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StudySets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FlashcardMode = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EverAssignedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HiddenByAdminAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HiddenReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClonedFromId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudySets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudySets_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserApiKeys",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EncryptedKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastFour = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AiNoticeAcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserApiKeys", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserApiKeys_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserSettingPresets",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSettingPresets", x => new { x.UserId, x.Scope });
                    table.ForeignKey(
                        name: "FK_UserSettingPresets_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClassMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClassroomId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeftAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InvitedById = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassMembers_Classrooms_ClassroomId",
                        column: x => x.ClassroomId,
                        principalTable: "Classrooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClassTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClassroomId = table.Column<int>(type: "int", nullable: false),
                    FromUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ToUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FromUserStays = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InvitedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassTransfers_AspNetUsers_FromUserId",
                        column: x => x.FromUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassTransfers_AspNetUsers_ToUserId",
                        column: x => x.ToUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassTransfers_Classrooms_ClassroomId",
                        column: x => x.ClassroomId,
                        principalTable: "Classrooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Invitations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ClassroomId = table.Column<int>(type: "int", nullable: false),
                    InviterId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RecipientId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    MemberId = table.Column<int>(type: "int", nullable: true),
                    TransferId = table.Column<int>(type: "int", nullable: true),
                    TransferFromUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    FromUserStays = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecipientNotifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invitations_Classrooms_ClassroomId",
                        column: x => x.ClassroomId,
                        principalTable: "Classrooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContentReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReporterId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TargetType = table.Column<int>(type: "int", nullable: false),
                    TargetId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TargetOwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StudySetId = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    HandledById = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    HandledAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentReports_AspNetUsers_HandledById",
                        column: x => x.HandledById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContentReports_AspNetUsers_ReporterId",
                        column: x => x.ReporterId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContentReports_AspNetUsers_TargetOwnerId",
                        column: x => x.TargetOwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContentReports_StudySets_StudySetId",
                        column: x => x.StudySetId,
                        principalTable: "StudySets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Flashcards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudySetId = table.Column<int>(type: "int", nullable: false),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    Front = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Back = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EditedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flashcards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Flashcards_StudySets_StudySetId",
                        column: x => x.StudySetId,
                        principalTable: "StudySets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Quizzes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EverAssignedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StudySetId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quizzes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Quizzes_StudySets_StudySetId",
                        column: x => x.StudySetId,
                        principalTable: "StudySets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourceTexts",
                columns: table => new
                {
                    StudySetId = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", maxLength: 50000, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceTexts", x => x.StudySetId);
                    table.ForeignKey(
                        name: "FK_SourceTexts_StudySets_StudySetId",
                        column: x => x.StudySetId,
                        principalTable: "StudySets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UnhideRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudySetId = table.Column<int>(type: "int", nullable: false),
                    RequesterId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DecidedById = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnhideRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnhideRequests_AspNetUsers_DecidedById",
                        column: x => x.DecidedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UnhideRequests_AspNetUsers_RequesterId",
                        column: x => x.RequesterId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UnhideRequests_StudySets_StudySetId",
                        column: x => x.StudySetId,
                        principalTable: "StudySets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdminAccessLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AdminId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ContentReportId = table.Column<int>(type: "int", nullable: false),
                    ViewedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminAccessLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminAccessLogs_AspNetUsers_AdminId",
                        column: x => x.AdminId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdminAccessLogs_ContentReports_ContentReportId",
                        column: x => x.ContentReportId,
                        principalTable: "ContentReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Assignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceLock = table.Column<bool>(type: "bit", nullable: false),
                    MonitorScreen = table.Column<bool>(type: "bit", nullable: false),
                    AutoSubmitLeaves = table.Column<int>(type: "int", nullable: true),
                    FlashcardMode = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CriticalThreshold = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CriticalQuestionsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CriticalQuestionIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "[]"),
                    IgnoreQuestionTimers = table.Column<bool>(type: "bit", nullable: false),
                    ClassroomId = table.Column<int>(type: "int", nullable: true),
                    CreatedById = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    QuizId = table.Column<int>(type: "int", nullable: true),
                    StudySetId = table.Column<int>(type: "int", nullable: true),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    Form = table.Column<int>(type: "int", nullable: false),
                    RequiredParts = table.Column<int>(type: "int", nullable: false),
                    OpensAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DueAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DurationMinutes = table.Column<int>(type: "int", nullable: true),
                    TestStartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TestEndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MaxAttempts = table.Column<int>(type: "int", nullable: true),
                    MasteryThreshold = table.Column<int>(type: "int", nullable: true),
                    AnswersPerQuestion = table.Column<int>(type: "int", nullable: true),
                    ScoreRetries = table.Column<bool>(type: "bit", nullable: false),
                    ScoreSelection = table.Column<int>(type: "int", nullable: false),
                    ScoreScale = table.Column<int>(type: "int", nullable: false),
                    Rounding = table.Column<int>(type: "int", nullable: false),
                    PassMark = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    ShuffleQuestions = table.Column<bool>(type: "bit", nullable: false),
                    ShuffleOptions = table.Column<bool>(type: "bit", nullable: false),
                    AudiencePolicy = table.Column<int>(type: "int", nullable: false),
                    StrangerPolicy = table.Column<int>(type: "int", nullable: false),
                    AllowAiAssistant = table.Column<bool>(type: "bit", nullable: false),
                    AllowAiAfterTest = table.Column<bool>(type: "bit", nullable: false),
                    AllowFlashcardsAfterDue = table.Column<bool>(type: "bit", nullable: false),
                    AllowQuizSelfReview = table.Column<bool>(type: "bit", nullable: false),
                    AllowCardSelfReview = table.Column<bool>(type: "bit", nullable: false),
                    ScoresPublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AnswersPublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SurveyResultsPublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CloseReason = table.Column<int>(type: "int", nullable: true),
                    ReportDeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assignments_Classrooms_ClassroomId",
                        column: x => x.ClassroomId,
                        principalTable: "Classrooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Assignments_Quizzes_QuizId",
                        column: x => x.QuizId,
                        principalTable: "Quizzes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Questions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    QuizId = table.Column<int>(type: "int", nullable: false),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Prompt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Points = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    TimeLimitSeconds = table.Column<int>(type: "int", nullable: true),
                    KeepOptionOrder = table.Column<bool>(type: "bit", nullable: false),
                    CaseSensitive = table.Column<bool>(type: "bit", nullable: false),
                    AcceptNoDiacritics = table.Column<bool>(type: "bit", nullable: false),
                    IgnorePunctuation = table.Column<bool>(type: "bit", nullable: false),
                    AcceptAnyBlankOrder = table.Column<bool>(type: "bit", nullable: false),
                    BlankScoring = table.Column<int>(type: "int", nullable: false),
                    NumberAnswerExact = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    NumberToleranceExact = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    NumberAnswer = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    NumberTolerance = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    MaxLength = table.Column<int>(type: "int", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Questions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Questions_Quizzes_QuizId",
                        column: x => x.QuizId,
                        principalTable: "Quizzes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssignmentChanges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssignmentId = table.Column<int>(type: "int", nullable: false),
                    ChangedById = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Field = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignmentChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssignmentChanges_Assignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "Assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Participants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FlashcardMode = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    AssignmentId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    GuestId = table.Column<int>(type: "int", nullable: true),
                    GuestDisplayName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    GuestStartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsStranger = table.Column<bool>(type: "bit", nullable: false),
                    ApprovalStatus = table.Column<int>(type: "int", nullable: false),
                    ExtraMinutes = table.Column<int>(type: "int", nullable: false),
                    ExtraMinutesReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExtraAttempts = table.Column<int>(type: "int", nullable: false),
                    ExtraAttemptsReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompletionStatus = table.Column<int>(type: "int", nullable: false),
                    CompletionLabels = table.Column<int>(type: "int", nullable: false),
                    MasteryReached = table.Column<bool>(type: "bit", nullable: false),
                    CardsCompletionFinalizedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Participants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Participants_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Participants_Assignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "Assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Participants_Guests_GuestId",
                        column: x => x.GuestId,
                        principalTable: "Guests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiConversations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StudySetId = table.Column<int>(type: "int", nullable: false),
                    AssignmentId = table.Column<int>(type: "int", nullable: true),
                    QuestionId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiConversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiConversations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiConversations_Assignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "Assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiConversations_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiConversations_StudySets_StudySetId",
                        column: x => x.StudySetId,
                        principalTable: "StudySets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuestionBlanks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QuestionId = table.Column<int>(type: "int", nullable: false),
                    BlankIndex = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionBlanks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionBlanks_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuestionOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QuestionId = table.Column<int>(type: "int", nullable: false),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionOptions_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuestionRevisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QuestionId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    EditKind = table.Column<int>(type: "int", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EditedById = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EditedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionRevisions_AspNetUsers_EditedById",
                        column: x => x.EditedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuestionRevisions_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Regrades",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssignmentId = table.Column<int>(type: "int", nullable: false),
                    QuestionId = table.Column<int>(type: "int", nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    NewCorrectJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ById = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Regrades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Regrades_AspNetUsers_ById",
                        column: x => x.ById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Regrades_Assignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "Assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Regrades_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FlashcardProgresses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HasTraditionalRating = table.Column<bool>(type: "bit", nullable: true),
                    TraditionalReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SrsBox = table.Column<int>(type: "int", nullable: true),
                    SrsDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ParticipantId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    FlashcardId = table.Column<int>(type: "int", nullable: false),
                    LastRating = table.Column<int>(type: "int", nullable: false),
                    FirstReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlashcardProgresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlashcardProgresses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FlashcardProgresses_Flashcards_FlashcardId",
                        column: x => x.FlashcardId,
                        principalTable: "Flashcards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FlashcardProgresses_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FlashcardReviews",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudySetId = table.Column<int>(type: "int", nullable: false),
                    FlashcardId = table.Column<int>(type: "int", nullable: false),
                    ParticipantId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    BeforeRating = table.Column<int>(type: "int", nullable: true),
                    AfterRating = table.Column<int>(type: "int", nullable: true),
                    BeforeBox = table.Column<int>(type: "int", nullable: false),
                    AfterBox = table.Column<int>(type: "int", nullable: false),
                    BeforeDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    AfterDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    WasDue = table.Column<bool>(type: "bit", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlashcardReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlashcardReviews_Flashcards_FlashcardId",
                        column: x => x.FlashcardId,
                        principalTable: "Flashcards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FlashcardReviews_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FlashcardReviews_StudySets_StudySetId",
                        column: x => x.StudySetId,
                        principalTable: "StudySets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RetakeRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParticipantId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    WindowStartsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WindowEndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedById = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AnswersWerePublished = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetakeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RetakeRequests_AspNetUsers_DecidedById",
                        column: x => x.DecidedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RetakeRequests_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConversationId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceVersion = table.Column<int>(type: "int", nullable: false),
                    QuestionId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiMessages_AiConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "AiConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiMessages_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BlankAnswers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QuestionBlankId = table.Column<int>(type: "int", nullable: false),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlankAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlankAnswers_QuestionBlanks_QuestionBlankId",
                        column: x => x.QuestionBlankId,
                        principalTable: "QuestionBlanks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Attempts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceConsentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LightHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FullHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    NetworkPrefix = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    MonitorScreen = table.Column<bool>(type: "bit", nullable: false),
                    AutoSubmitLeaves = table.Column<int>(type: "int", nullable: true),
                    MonitorHeartbeatAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MonitorWindowAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MonitorEventCount = table.Column<int>(type: "int", nullable: false),
                    LeaveBellSent = table.Column<bool>(type: "bit", nullable: false),
                    CriticalLabel = table.Column<int>(type: "int", nullable: false),
                    CriticalReasons = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FailedCriticalQuestion = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    ParticipantId = table.Column<int>(type: "int", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    MadeAsGuest = table.Column<bool>(type: "bit", nullable: false),
                    ExcludeFromGradebook = table.Column<bool>(type: "bit", nullable: false),
                    RetakeRequestId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EndReason = table.Column<int>(type: "int", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeadlineAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActiveDeviceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DisconnectedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FirstScoreExact = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FirstScore = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    PracticeScore = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    MaxScore = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    MasteredCount = table.Column<int>(type: "int", nullable: false),
                    MasteryReachedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CancelledById = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Attempts_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Attempts_RetakeRequests_RetakeRequestId",
                        column: x => x.RetakeRequestId,
                        principalTable: "RetakeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiCitations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AiMessageId = table.Column<int>(type: "int", nullable: false),
                    ExcerptText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartOffset = table.Column<int>(type: "int", nullable: false),
                    EndOffset = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiCitations_AiMessages_AiMessageId",
                        column: x => x.AiMessageId,
                        principalTable: "AiMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttemptQuestions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttemptId = table.Column<long>(type: "bigint", nullable: false),
                    QuestionId = table.Column<int>(type: "int", nullable: false),
                    QuestionVersion = table.Column<int>(type: "int", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    OptionOrderJson = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    Flagged = table.Column<bool>(type: "bit", nullable: false),
                    AnswerCount = table.Column<int>(type: "int", nullable: false),
                    IsMastered = table.Column<bool>(type: "bit", nullable: false),
                    FirstScoreExact = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LatestScoreExact = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FirstScore = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    LatestScore = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    TimerStartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TimeLeftSeconds = table.Column<int>(type: "int", nullable: true),
                    DraftJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DraftSavedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttemptQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttemptQuestions_Attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "Attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttemptQuestions_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeviceChangeRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttemptId = table.Column<long>(type: "bigint", nullable: false),
                    RequestingDeviceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedById = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    LightHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FullHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    NetworkPrefix = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceChangeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceChangeRequests_Attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "Attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScoreHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttemptId = table.Column<long>(type: "bigint", nullable: false),
                    RegradeId = table.Column<long>(type: "bigint", nullable: false),
                    OldFirstScore = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NewFirstScore = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OldPracticeScore = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    NewPracticeScore = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OldMaxScore = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NewMaxScore = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoreHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScoreHistories_Attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "Attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScoreHistories_Regrades_RegradeId",
                        column: x => x.RegradeId,
                        principalTable: "Regrades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScreenLeaves",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttemptId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DurationSeconds = table.Column<int>(type: "int", nullable: false),
                    Counted = table.Column<bool>(type: "bit", nullable: false),
                    ReportedOffline = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreenLeaves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScreenLeaves_Attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "Attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AnswerSubmissions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttemptQuestionId = table.Column<long>(type: "bigint", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: true),
                    ScoreExact = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Score = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    IsFirstAnswer = table.Column<bool>(type: "bit", nullable: false),
                    IsFinal = table.Column<bool>(type: "bit", nullable: false),
                    PublishedToClass = table.Column<bool>(type: "bit", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnswerSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnswerSubmissions_AttemptQuestions_AttemptQuestionId",
                        column: x => x.AttemptQuestionId,
                        principalTable: "AttemptQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountLocks_LockedById",
                table: "AccountLocks",
                column: "LockedById");

            migrationBuilder.CreateIndex(
                name: "IX_AccountLocks_UnlockedById",
                table: "AccountLocks",
                column: "UnlockedById");

            migrationBuilder.CreateIndex(
                name: "IX_AccountLocks_UserId_UnlockedAt",
                table: "AccountLocks",
                columns: new[] { "UserId", "UnlockedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminAccessLogs_AdminId",
                table: "AdminAccessLogs",
                column: "AdminId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAccessLogs_ContentReportId",
                table: "AdminAccessLogs",
                column: "ContentReportId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminRoleChanges_AdminId",
                table: "AdminRoleChanges",
                column: "AdminId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminRoleChanges_TargetUserId",
                table: "AdminRoleChanges",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AiCitations_AiMessageId",
                table: "AiCitations",
                column: "AiMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_AiConversations_AssignmentId",
                table: "AiConversations",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AiConversations_QuestionId",
                table: "AiConversations",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_AiConversations_StudySetId",
                table: "AiConversations",
                column: "StudySetId");

            migrationBuilder.CreateIndex(
                name: "IX_AiConversations_UserId_DeletedAt",
                table: "AiConversations",
                columns: new[] { "UserId", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiMessages_ConversationId",
                table: "AiMessages",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_AiMessages_QuestionId",
                table: "AiMessages",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_AnswerSubmissions_AttemptQuestionId",
                table: "AnswerSubmissions",
                column: "AttemptQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_AnswerSubmissions_ClientRequestId",
                table: "AnswerSubmissions",
                column: "ClientRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserPasskeys_UserId",
                table: "AspNetUserPasskeys",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentChanges_AssignmentId",
                table: "AssignmentChanges",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_ClassroomId",
                table: "Assignments",
                column: "ClassroomId");

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_QuizId",
                table: "Assignments",
                column: "QuizId");

            migrationBuilder.CreateIndex(
                name: "IX_AttemptQuestions_AttemptId",
                table: "AttemptQuestions",
                column: "AttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_AttemptQuestions_QuestionId",
                table: "AttemptQuestions",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_OneOpenPerParticipant",
                table: "Attempts",
                column: "ParticipantId",
                unique: true,
                filter: "[Status] IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_ParticipantId_AttemptNumber",
                table: "Attempts",
                columns: new[] { "ParticipantId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_RetakeRequestId",
                table: "Attempts",
                column: "RetakeRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Action_At",
                table: "AuditEntries",
                columns: new[] { "Action", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_ActorUserId_At",
                table: "AuditEntries",
                columns: new[] { "ActorUserId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_At",
                table: "AuditEntries",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_RequestId",
                table: "AuditEntries",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_BlankAnswers_QuestionBlankId_OrderIndex",
                table: "BlankAnswers",
                columns: new[] { "QuestionBlankId", "OrderIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassMembers_ClassroomId_UserId",
                table: "ClassMembers",
                columns: new[] { "ClassroomId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassMembers_OneMainLecturer",
                table: "ClassMembers",
                column: "ClassroomId",
                unique: true,
                filter: "[Role] = 1 AND [Status] = 3");

            migrationBuilder.CreateIndex(
                name: "IX_ClassMembers_UserId",
                table: "ClassMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassTransfers_FromUserId",
                table: "ClassTransfers",
                column: "FromUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassTransfers_OnePendingPerClass",
                table: "ClassTransfers",
                column: "ClassroomId",
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ClassTransfers_ToUserId",
                table: "ClassTransfers",
                column: "ToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentReports_HandledById",
                table: "ContentReports",
                column: "HandledById");

            migrationBuilder.CreateIndex(
                name: "IX_ContentReports_ReporterId_TargetType_TargetId",
                table: "ContentReports",
                columns: new[] { "ReporterId", "TargetType", "TargetId" },
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ContentReports_StudySetId",
                table: "ContentReports",
                column: "StudySetId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentReports_TargetOwnerId",
                table: "ContentReports",
                column: "TargetOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceChangeRequests_AttemptId",
                table: "DeviceChangeRequests",
                column: "AttemptId",
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardProgresses_FlashcardId",
                table: "FlashcardProgresses",
                column: "FlashcardId");

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardProgresses_ParticipantId_FlashcardId",
                table: "FlashcardProgresses",
                columns: new[] { "ParticipantId", "FlashcardId" },
                unique: true,
                filter: "[ParticipantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardProgresses_UserId_FlashcardId",
                table: "FlashcardProgresses",
                columns: new[] { "UserId", "FlashcardId" },
                unique: true,
                filter: "[ParticipantId] IS NULL AND [UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardReviews_FlashcardId",
                table: "FlashcardReviews",
                column: "FlashcardId");

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardReviews_ParticipantId_StudySetId_ReviewedAt",
                table: "FlashcardReviews",
                columns: new[] { "ParticipantId", "StudySetId", "ReviewedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardReviews_StudySetId",
                table: "FlashcardReviews",
                column: "StudySetId");

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardReviews_UserId_StudySetId_ReviewedAt",
                table: "FlashcardReviews",
                columns: new[] { "UserId", "StudySetId", "ReviewedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Flashcards_StudySetId",
                table: "Flashcards",
                column: "StudySetId");

            migrationBuilder.CreateIndex(
                name: "IX_Guests_BrowserTokenHash",
                table: "Guests",
                column: "BrowserTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Guests_MergedIntoUserId",
                table: "Guests",
                column: "MergedIntoUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Guests_UserId",
                table: "Guests",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_ClassroomId_Kind_Status",
                table: "Invitations",
                columns: new[] { "ClassroomId", "Kind", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_InviterId_CreatedAt",
                table: "Invitations",
                columns: new[] { "InviterId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_NormalizedEmail_Status_ExpiresAt",
                table: "Invitations",
                columns: new[] { "NormalizedEmail", "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_JoinCodeFailures_KeyType_KeyHash",
                table: "JoinCodeFailures",
                columns: new[] { "KeyType", "KeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JoinCodes_Code",
                table: "JoinCodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JoinCodes_OneActivePerTarget",
                table: "JoinCodes",
                columns: new[] { "TargetType", "TargetId" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_KnownDevices_UserId_DeviceHash",
                table: "KnownDevices",
                columns: new[] { "UserId", "DeviceHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_ReadAt_Id",
                table: "Notifications",
                columns: new[] { "UserId", "ReadAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Participants_AssignmentId_GuestId",
                table: "Participants",
                columns: new[] { "AssignmentId", "GuestId" },
                unique: true,
                filter: "[GuestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Participants_AssignmentId_UserId",
                table: "Participants",
                columns: new[] { "AssignmentId", "UserId" },
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Participants_GuestId",
                table: "Participants",
                column: "GuestId");

            migrationBuilder.CreateIndex(
                name: "IX_Participants_UserId",
                table: "Participants",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionBlanks_QuestionId_BlankIndex",
                table: "QuestionBlanks",
                columns: new[] { "QuestionId", "BlankIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuestionOptions_QuestionId",
                table: "QuestionOptions",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionRevisions_EditedById",
                table: "QuestionRevisions",
                column: "EditedById");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionRevisions_QuestionId_Version",
                table: "QuestionRevisions",
                columns: new[] { "QuestionId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Questions_QuizId",
                table: "Questions",
                column: "QuizId");

            migrationBuilder.CreateIndex(
                name: "IX_Quizzes_StudySetId",
                table: "Quizzes",
                column: "StudySetId");

            migrationBuilder.CreateIndex(
                name: "IX_Regrades_AssignmentId_ClientRequestId",
                table: "Regrades",
                columns: new[] { "AssignmentId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Regrades_ById",
                table: "Regrades",
                column: "ById");

            migrationBuilder.CreateIndex(
                name: "IX_Regrades_QuestionId",
                table: "Regrades",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReminderDigests_UserId_VietnamDate",
                table: "ReminderDigests",
                columns: new[] { "UserId", "VietnamDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reminders_EmailedAt_Id",
                table: "Reminders",
                columns: new[] { "EmailedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Reminders_UserId_Kind_TargetId",
                table: "Reminders",
                columns: new[] { "UserId", "Kind", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RetakeRequests_DecidedById",
                table: "RetakeRequests",
                column: "DecidedById");

            migrationBuilder.CreateIndex(
                name: "IX_RetakeRequests_OnePendingPerParticipant",
                table: "RetakeRequests",
                column: "ParticipantId",
                unique: true,
                filter: "[Status] IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_ScoreHistories_AttemptId",
                table: "ScoreHistories",
                column: "AttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_ScoreHistories_RegradeId_AttemptId",
                table: "ScoreHistories",
                columns: new[] { "RegradeId", "AttemptId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScreenLeaves_AttemptId_Kind",
                table: "ScreenLeaves",
                columns: new[] { "AttemptId", "Kind" },
                unique: true,
                filter: "[EndedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ScreenLeaves_AttemptId_StartedAt",
                table: "ScreenLeaves",
                columns: new[] { "AttemptId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StudySets_OwnerId",
                table: "StudySets",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_UnhideRequests_DecidedById",
                table: "UnhideRequests",
                column: "DecidedById");

            migrationBuilder.CreateIndex(
                name: "IX_UnhideRequests_RequesterId",
                table: "UnhideRequests",
                column: "RequesterId");

            migrationBuilder.CreateIndex(
                name: "IX_UnhideRequests_StudySetId",
                table: "UnhideRequests",
                column: "StudySetId",
                unique: true,
                filter: "[Status] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountLocks");

            migrationBuilder.DropTable(
                name: "AdminAccessLogs");

            migrationBuilder.DropTable(
                name: "AdminRoleChanges");

            migrationBuilder.DropTable(
                name: "AiCitations");

            migrationBuilder.DropTable(
                name: "AnswerSubmissions");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserPasskeys");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AssignmentChanges");

            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "BlankAnswers");

            migrationBuilder.DropTable(
                name: "ClassMembers");

            migrationBuilder.DropTable(
                name: "ClassTransfers");

            migrationBuilder.DropTable(
                name: "DeviceChangeRequests");

            migrationBuilder.DropTable(
                name: "FlashcardProgresses");

            migrationBuilder.DropTable(
                name: "FlashcardReviews");

            migrationBuilder.DropTable(
                name: "Invitations");

            migrationBuilder.DropTable(
                name: "JoinCodeFailures");

            migrationBuilder.DropTable(
                name: "JoinCodes");

            migrationBuilder.DropTable(
                name: "KnownDevices");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "QuestionOptions");

            migrationBuilder.DropTable(
                name: "QuestionRevisions");

            migrationBuilder.DropTable(
                name: "ReminderDigests");

            migrationBuilder.DropTable(
                name: "Reminders");

            migrationBuilder.DropTable(
                name: "ScoreHistories");

            migrationBuilder.DropTable(
                name: "ScreenLeaves");

            migrationBuilder.DropTable(
                name: "SourceTexts");

            migrationBuilder.DropTable(
                name: "UnhideRequests");

            migrationBuilder.DropTable(
                name: "UserApiKeys");

            migrationBuilder.DropTable(
                name: "UserSettingPresets");

            migrationBuilder.DropTable(
                name: "ContentReports");

            migrationBuilder.DropTable(
                name: "AiMessages");

            migrationBuilder.DropTable(
                name: "AttemptQuestions");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "QuestionBlanks");

            migrationBuilder.DropTable(
                name: "Flashcards");

            migrationBuilder.DropTable(
                name: "Regrades");

            migrationBuilder.DropTable(
                name: "AiConversations");

            migrationBuilder.DropTable(
                name: "Attempts");

            migrationBuilder.DropTable(
                name: "Questions");

            migrationBuilder.DropTable(
                name: "RetakeRequests");

            migrationBuilder.DropTable(
                name: "Participants");

            migrationBuilder.DropTable(
                name: "Assignments");

            migrationBuilder.DropTable(
                name: "Guests");

            migrationBuilder.DropTable(
                name: "Classrooms");

            migrationBuilder.DropTable(
                name: "Quizzes");

            migrationBuilder.DropTable(
                name: "StudySets");

            migrationBuilder.DropTable(
                name: "AspNetUsers");
        }
    }
}
