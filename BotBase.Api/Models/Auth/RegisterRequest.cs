namespace BotBase.Api.Models.Auth;

// AcceptTerms — галочка «Принимаю условия и политику конфиденциальности»; без неё аккаунт не создаётся.
public record RegisterRequest(string Email, string Password, string BusinessName, bool AcceptTerms = false);
