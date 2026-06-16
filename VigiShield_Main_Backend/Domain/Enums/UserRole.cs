namespace VigiShield.Domain.Enums;

public enum UserRole
{
    Primary,
    Secondary,
    // Developer/administrator account. Has every Primary power plus access to the
    // hidden in-app developer tools (role preview, server URL, admin management).
    Admin
}
