namespace SoMan.Models;

public enum AccountStatus
{
    Active,
    Suspended,
    NeedVerification,
    CookiesExpired,
    Error,
    Disabled
}

public enum Platform
{
    Threads,
    Facebook,
    Instagram,
    Pinterest,
    Twitter
}
