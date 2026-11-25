namespace MindSprintAI_ASP.NET_MVC.Models;

public record TwoFactorStatus(bool Enabled, bool Authenticator, bool Email, bool Admin, bool HasPassword, int RecoveryCodes);
public record AuthenticatorSetup(string Key, string Uri);
public record TwoFactorLoginPage(bool Authenticator, bool Email, bool Passkey = false, bool Recovery = true);
