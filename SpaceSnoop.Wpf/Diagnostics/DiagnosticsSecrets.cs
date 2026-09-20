using System.Text.RegularExpressions;

namespace SpaceSnoop.Wpf.Diagnostics;

/// <summary>
/// Вымарывание секретов из текста записи пакета диагностики: значение справа от <c>:</c> или
/// <c>=</c> у ключа, чьё имя содержит <c>token</c>, <c>secret</c>, <c>password</c>,
/// <c>credential</c> или <c>api_key</c>, заменяется маской <see cref="Mask"/>.
/// <para>
/// Пара «имя – значение» ищется <b>в любом месте строки</b>. Пакет собирается в первую очередь из
/// логов, а там перед именем поля стоит отметка времени и уровень
/// (<c>2026-09-20 18:00:11.234 +03:00 [INF] …</c>), само значение лежит в компактном JSON посреди
/// сообщения, а то и просто в свободном тексте.
/// </para>
/// <para>
/// Границу значения задаёт его вид: у строки в кавычках – закрывающая кавычка, у голого значения –
/// пробел, запятая, точка с запятой, амперсанд или закрывающая скобка. Остаток строки поэтому не
/// съедается, а переводы строки исключены из каждого класса, так что совпадение не выходит за
/// пределы своей строки и не зависит от того, CRLF в файле или LF.
/// </para>
/// <para>
/// Ретроспектива слева от имени – потолок стоимости, а не украшение: без неё старт совпадения
/// пробуется с каждого символа длинного слова и стоимость растёт квадратично от его длины.
/// </para>
/// </summary>
public static partial class DiagnosticsSecrets
{
    /// <summary>Чем заменяется секрет.</summary>
    public const string Mask = "<СЕКРЕТ>";

    private const string KeyChar = @"[A-Za-z0-9_.\-]";

    private const string Separator = @"[^\S\r\n]*[:=][^\S\r\n]*";

    private const string Value = @"(?:""(?<quoted>[^""\r\n]*)""|'(?<ticked>[^'\r\n]*)'|(?<bare>[^\s,;&}\])""']+))";

    private const string Names = @"token|secret|password|credential|api[_\-]?key";

    private const string Pattern = $@"(?<!{KeyChar})(?<head>{KeyChar}*(?:{Names}){KeyChar}*[""']?{Separator}){Value}";

    /// <summary>Вымарывает секреты в тексте одной записи пакета.</summary>
    public static string Redact(string text)
    {
        return string.IsNullOrEmpty(text) ? text : SecretPattern().Replace(text, Replace);
    }

    private static string Replace(Match match)
    {
        var head = match.Groups["head"].Value;

        if (match.Groups["quoted"].Success)
        {
            return string.Concat(head, '"', Mask, '"');
        }

        return match.Groups["ticked"].Success
            ? string.Concat(head, '\'', Mask, '\'')
            : string.Concat(head, Mask);
    }

    [GeneratedRegex(Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 5000)]
    private static partial Regex SecretPattern();
}
