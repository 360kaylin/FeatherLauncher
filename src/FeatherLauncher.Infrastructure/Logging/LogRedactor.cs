using System.Text.RegularExpressions;
using FeatherLauncher.Core.Services;

namespace FeatherLauncher.Infrastructure.Logging;

public sealed partial class LogRedactor : ILogRedactor
{
    [GeneratedRegex("(?i)(password|passwd|access[_-]?token|refresh[_-]?token|xbox[_-]?(?:user[_-]?)?token|xsts[_-]?token|device[_-]?(?:code|user[_-]?code)|authorization|account[_-]?id|xuid|email|minecraft[_-]?uuid)\\s*[=:]\\s*((?:Bearer\\s+)?[^\\s,;]+)")]
    private static partial Regex SecretPattern();
    [GeneratedRegex("(?i)bearer\\s+[A-Za-z0-9._~+/-]+=*")]
    private static partial Regex BearerPattern();
    [GeneratedRegex(@"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b")]
    private static partial Regex EmailPattern();
    [GeneratedRegex(@"(?i)\b(?:eyJ[A-Za-z0-9_-]{10,}|[A-F0-9]{32}|[A-Z0-9]{4,}-[A-Z0-9-]{4,})\b")]
    private static partial Regex OpaqueSecretPattern();
    public string Redact(string message) => OpaqueSecretPattern().Replace(EmailPattern().Replace(BearerPattern().Replace(SecretPattern().Replace(message, "$1=[REDACTED]"), "Bearer [REDACTED]"), "[REDACTED]"), "[REDACTED]");
}
