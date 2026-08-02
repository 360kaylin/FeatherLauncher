using System.Text.RegularExpressions;
using FeatherLauncher.Core.Services;

namespace FeatherLauncher.Infrastructure.Logging;

public sealed partial class LogRedactor : ILogRedactor
{
    [GeneratedRegex("(?i)(password|passwd|access[_-]?token|refresh[_-]?token|xbox[_-]?(?:user[_-]?)?token|xsts[_-]?token|device[_-]?(?:code|user[_-]?code)|authorization|account[_-]?id|xuid|email|minecraft[_-]?uuid)\\s*[=:]\\s*((?:Bearer\\s+)?[^\\s,;]+)")]
    private static partial Regex SecretPattern();
    [GeneratedRegex("(?i)bearer\\s+[A-Za-z0-9._~+/-]+=*")]
    private static partial Regex BearerPattern();
    public string Redact(string message) => BearerPattern().Replace(SecretPattern().Replace(message, "$1=[REDACTED]"), "Bearer [REDACTED]");
}
