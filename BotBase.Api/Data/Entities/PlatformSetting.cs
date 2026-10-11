namespace BotBase.Api.Data.Entities;

// Настройки платформы в виде ключ-значение (email техподдержки, чат для уведомлений и т.п.)
public class PlatformSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
