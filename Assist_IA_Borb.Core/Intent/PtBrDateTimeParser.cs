using System.Globalization;
using System.Text.RegularExpressions;

namespace Assist_IA_Borb.Core.Intent;

/// <summary>
/// Interpreta expressões de data e hora em português falado/digitado, do jeito que
/// uma pessoa realmente escreve: "10:00", "10 horas", "dez horas", "3 da tarde",
/// "meio-dia", "15 horas de hoje", "amanhã às 9", "sexta às 14h30".
///
/// Devolve também o texto RESTANTE (sem os trechos de data/hora), pra quem chamou
/// poder usar isso como título do evento/alarme sem repetir "para as 15 horas de hoje".
/// </summary>
public static class PtBrDateTimeParser
{
    public sealed class ParseResult
    {
        public DateTime? DateTime { get; init; }
        public bool HasExplicitTime { get; init; }
        public string RemainingText { get; init; } = string.Empty;
    }

    private static readonly Dictionary<string, int> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zero"] = 0, ["uma"] = 1, ["um"] = 1, ["duas"] = 2, ["dois"] = 2,
        ["tres"] = 3, ["três"] = 3, ["quatro"] = 4, ["cinco"] = 5, ["seis"] = 6,
        ["sete"] = 7, ["oito"] = 8, ["nove"] = 9, ["dez"] = 10, ["onze"] = 11,
        ["doze"] = 12, ["treze"] = 13, ["quatorze"] = 14, ["catorze"] = 14,
        ["quinze"] = 15, ["dezesseis"] = 16, ["dezessete"] = 17, ["dezoito"] = 18,
        ["dezenove"] = 19, ["vinte"] = 20, ["vinte e uma"] = 21, ["vinte e um"] = 21,
        ["vinte e duas"] = 22, ["vinte e dois"] = 22, ["vinte e tres"] = 23, ["vinte e três"] = 23,
    };

    private static readonly Dictionary<string, DayOfWeek> WeekDays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["domingo"] = DayOfWeek.Sunday,
        ["segunda"] = DayOfWeek.Monday, ["segunda-feira"] = DayOfWeek.Monday,
        ["terça"] = DayOfWeek.Tuesday, ["terca"] = DayOfWeek.Tuesday, ["terça-feira"] = DayOfWeek.Tuesday,
        ["quarta"] = DayOfWeek.Wednesday, ["quarta-feira"] = DayOfWeek.Wednesday,
        ["quinta"] = DayOfWeek.Thursday, ["quinta-feira"] = DayOfWeek.Thursday,
        ["sexta"] = DayOfWeek.Friday, ["sexta-feira"] = DayOfWeek.Friday,
        ["sábado"] = DayOfWeek.Saturday, ["sabado"] = DayOfWeek.Saturday,
    };

    private static readonly Dictionary<string, int> Months = new(StringComparer.OrdinalIgnoreCase)
    {
        ["janeiro"] = 1, ["fevereiro"] = 2, ["março"] = 3, ["marco"] = 3, ["abril"] = 4,
        ["maio"] = 5, ["junho"] = 6, ["julho"] = 7, ["agosto"] = 8, ["setembro"] = 9,
        ["outubro"] = 10, ["novembro"] = 11, ["dezembro"] = 12,
    };

    /// <param name="now">Momento de referência - injetável para facilitar testes.</param>
    public static ParseResult Parse(string input, DateTime? now = null)
    {
        var reference = now ?? System.DateTime.Now;
        var working = input ?? string.Empty;

        var (timeOfDay, hasTime, afterTime) = ExtractTime(working);
        var (date, afterDate) = ExtractDate(afterTime, reference);

        if (!hasTime && date is null)
        {
            return new ParseResult { RemainingText = Cleanup(afterDate) };
        }

        // Sem hora explícita: assume o próprio horário atual como base (evento "o dia todo"
        // fica a cargo de quem chamou decidir).
        var baseDate = date ?? reference.Date;
        var result = hasTime
            ? baseDate.Date + timeOfDay
            : baseDate;

        // Se a pessoa falou só a hora e ela já passou hoje, assume que quis dizer amanhã -
        // é o comportamento esperado de um alarme ("me acorda às 7" às 22h = amanhã 7h).
        if (hasTime && date is null && result <= reference)
        {
            result = result.AddDays(1);
        }

        return new ParseResult
        {
            DateTime = result,
            HasExplicitTime = hasTime,
            RemainingText = Cleanup(afterDate)
        };
    }

    // ── Hora ───────────────────────────────────────────────────────────

    private static (TimeSpan Time, bool Found, string Remaining) ExtractTime(string text)
    {
        // 1) Formatos numéricos: "10:00", "14h30", "9h", "10 horas", "15hs"
        var numeric = Regex.Match(
            text,
            @"\b(?<h>\d{1,2})\s*(?::|h(?:oras?|s)?)\s*(?<m>\d{2})?\b(?:\s*(?:horas?|hs))?",
            RegexOptions.IgnoreCase);

        if (numeric.Success)
        {
            var h = int.Parse(numeric.Groups["h"].Value);
            var m = numeric.Groups["m"].Success ? int.Parse(numeric.Groups["m"].Value) : 0;
            var rest = text.Remove(numeric.Index, numeric.Length);
            (h, rest) = ApplyPeriodOfDay(h, rest);
            (m, rest) = ApplyHalfHour(m, rest);

            if (h is >= 0 and <= 23 && m is >= 0 and <= 59)
            {
                return (new TimeSpan(h, m, 0), true, rest);
            }
        }

        // 2) "meio-dia" / "meia-noite"
        var noon = Regex.Match(text, @"\bmeio[\s-]?dia\b", RegexOptions.IgnoreCase);
        if (noon.Success)
        {
            var rest = text.Remove(noon.Index, noon.Length);
            var (mm, rest2) = ApplyHalfHour(0, rest);
            return (new TimeSpan(12, mm, 0), true, rest2);
        }

        var midnight = Regex.Match(text, @"\bmeia[\s-]?noite\b", RegexOptions.IgnoreCase);
        if (midnight.Success)
        {
            var rest = text.Remove(midnight.Index, midnight.Length);
            return (new TimeSpan(0, 0, 0), true, rest);
        }

        // 3) Número "solto" que claramente é hora pelo contexto:
        //    "às 7", "as 7 da manhã", "7 da noite" - sem ":" nem "h" no meio.
        var bare = Regex.Match(
            text,
            @"(?:\b(?:às|as|à|a)\s+)(?<h>\d{1,2})\b(?!\s*[:/h\d])|" +
            @"\b(?<h2>\d{1,2})\s+(?:da|de)\s+(?:manhã|manha|tarde|noite|madrugada)\b",
            RegexOptions.IgnoreCase);

        if (bare.Success)
        {
            var raw = bare.Groups["h"].Success ? bare.Groups["h"].Value : bare.Groups["h2"].Value;
            var h = int.Parse(raw);

            if (h is >= 0 and <= 23)
            {
                // Preserva o "da tarde/noite" no texto pra ApplyPeriodOfDay poder usar,
                // removendo só o número em si.
                var numIndex = bare.Groups["h"].Success ? bare.Groups["h"].Index : bare.Groups["h2"].Index;
                var numLength = bare.Groups["h"].Success ? bare.Groups["h"].Length : bare.Groups["h2"].Length;
                var rest = text.Remove(numIndex, numLength);

                (h, rest) = ApplyPeriodOfDay(h, rest);
                var (m2, rest2) = ApplyHalfHour(0, rest);
                return (new TimeSpan(h % 24, m2, 0), true, rest2);
            }
        }

        // 4) Números por extenso: "dez horas", "às três da tarde"
        foreach (var (word, value) in NumberWords.OrderByDescending(kv => kv.Key.Length))
        {
            var pattern = $@"\b{Regex.Escape(word)}\b(?:\s*(?:horas?|hs))?";
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);

            // Só aceita se vier acompanhado de "hora(s)" ou de um período do dia,
            // pra não confundir "dois relatórios" com horário.
            var looksLikeTime = Regex.IsMatch(match.Value, @"horas?|hs", RegexOptions.IgnoreCase)
                                || Regex.IsMatch(text, @"\b(da|de)\s+(manhã|manha|tarde|noite)\b", RegexOptions.IgnoreCase);

            if (match.Success && looksLikeTime)
            {
                var rest = text.Remove(match.Index, match.Length);
                var (h, rest2) = ApplyPeriodOfDay(value, rest);
                var (m, rest3) = ApplyHalfHour(0, rest2);
                return (new TimeSpan(h % 24, m, 0), true, rest3);
            }
        }

        return (TimeSpan.Zero, false, text);
    }

    /// <summary>"3 da tarde" -> 15h; "8 da noite" -> 20h; "7 da manhã" -> 7h.</summary>
    private static (int Hour, string Remaining) ApplyPeriodOfDay(int hour, string text)
    {
        var m = Regex.Match(text, @"\b(?:da|de|à|a)\s*(?<p>manhã|manha|tarde|noite|madrugada)\b",
            RegexOptions.IgnoreCase);

        if (!m.Success)
        {
            return (hour, text);
        }

        var period = m.Groups["p"].Value.ToLowerInvariant();
        var rest = text.Remove(m.Index, m.Length);

        var adjusted = period switch
        {
            "tarde" when hour < 12 => hour + 12,
            "noite" when hour < 12 => hour + 12,
            "madrugada" when hour == 12 => 0,
            "manhã" or "manha" when hour == 12 => 0,
            _ => hour
        };

        return (adjusted, rest);
    }

    /// <summary>"e meia" -> +30min; "e quinze" -> +15min.</summary>
    private static (int Minutes, string Remaining) ApplyHalfHour(int minutes, string text)
    {
        var half = Regex.Match(text, @"\se\s+meia\b", RegexOptions.IgnoreCase);
        if (half.Success)
        {
            return (30, text.Remove(half.Index, half.Length));
        }

        var quarter = Regex.Match(text, @"\se\s+quinze\b", RegexOptions.IgnoreCase);
        if (quarter.Success)
        {
            return (15, text.Remove(quarter.Index, quarter.Length));
        }

        return (minutes, text);
    }

    // ── Data ───────────────────────────────────────────────────────────

    private static (DateTime? Date, string Remaining) ExtractDate(string text, DateTime reference)
    {
        // "depois de amanhã" antes de "amanhã", senão sobra "depois de"
        var dayAfter = Regex.Match(text, @"\bdepois\s+de\s+amanh[ãa]\b", RegexOptions.IgnoreCase);
        if (dayAfter.Success)
        {
            return (reference.Date.AddDays(2), text.Remove(dayAfter.Index, dayAfter.Length));
        }

        var tomorrow = Regex.Match(text, @"\bamanh[ãa]\b", RegexOptions.IgnoreCase);
        if (tomorrow.Success)
        {
            return (reference.Date.AddDays(1), text.Remove(tomorrow.Index, tomorrow.Length));
        }

        var today = Regex.Match(text, @"\b(?:de\s+)?hoje\b", RegexOptions.IgnoreCase);
        if (today.Success)
        {
            return (reference.Date, text.Remove(today.Index, today.Length));
        }

        // "15/08", "15/08/2026"
        var slash = Regex.Match(text, @"\b(?<d>\d{1,2})/(?<m>\d{1,2})(?:/(?<y>\d{2,4}))?\b");
        if (slash.Success)
        {
            var d = int.Parse(slash.Groups["d"].Value);
            var mo = int.Parse(slash.Groups["m"].Value);
            var y = slash.Groups["y"].Success ? NormalizeYear(int.Parse(slash.Groups["y"].Value)) : reference.Year;

            if (TryBuildDate(y, mo, d, out var parsed))
            {
                return (parsed, text.Remove(slash.Index, slash.Length));
            }
        }

        // "dia 15 de agosto", "15 de agosto"
        var byMonth = Regex.Match(
            text,
            @"\b(?:dia\s+)?(?<d>\d{1,2})\s+de\s+(?<mes>janeiro|fevereiro|mar[çc]o|abril|maio|junho|julho|agosto|setembro|outubro|novembro|dezembro)\b",
            RegexOptions.IgnoreCase);

        if (byMonth.Success && Months.TryGetValue(byMonth.Groups["mes"].Value, out var monthNum))
        {
            var d = int.Parse(byMonth.Groups["d"].Value);
            if (TryBuildDate(reference.Year, monthNum, d, out var parsed))
            {
                // Data já passou este ano? Assume o ano que vem.
                if (parsed < reference.Date)
                {
                    parsed = parsed.AddYears(1);
                }

                return (parsed, text.Remove(byMonth.Index, byMonth.Length));
            }
        }

        // Dias da semana: "sexta", "na segunda-feira" -> próxima ocorrência
        foreach (var (word, dow) in WeekDays.OrderByDescending(kv => kv.Key.Length))
        {
            var m = Regex.Match(text, $@"\b(?:na|no|essa|esta|próxima|proxima)?\s*{Regex.Escape(word)}\b",
                RegexOptions.IgnoreCase);

            if (!m.Success)
            {
                continue;
            }

            var delta = ((int)dow - (int)reference.DayOfWeek + 7) % 7;
            if (delta == 0)
            {
                delta = 7; // "sexta" numa sexta = a próxima
            }

            return (reference.Date.AddDays(delta), text.Remove(m.Index, m.Length));
        }

        // "dia 15" (mês corrente, ou o próximo se já passou)
        var dayOnly = Regex.Match(text, @"\bdia\s+(?<d>\d{1,2})\b", RegexOptions.IgnoreCase);
        if (dayOnly.Success)
        {
            var d = int.Parse(dayOnly.Groups["d"].Value);
            if (TryBuildDate(reference.Year, reference.Month, d, out var parsed))
            {
                if (parsed < reference.Date)
                {
                    parsed = parsed.AddMonths(1);
                }

                return (parsed, text.Remove(dayOnly.Index, dayOnly.Length));
            }
        }

        return (null, text);
    }

    private static bool TryBuildDate(int year, int month, int day, out DateTime result)
    {
        result = default;

        if (month is < 1 or > 12 || day < 1)
        {
            return false;
        }

        if (day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        result = new DateTime(year, month, day);
        return true;
    }

    private static int NormalizeYear(int year) => year < 100 ? 2000 + year : year;

    // ── Limpeza do texto restante ──────────────────────────────────────

    private static string Cleanup(string text)
    {
        var cleaned = Regex.Replace(text,
            @"\b(para|pra|as|às|à|a|no|na|em|de|do|da|um|uma|o|s)\b\s*$",
            "", RegexOptions.IgnoreCase);

        cleaned = Regex.Replace(cleaned, @"^\s*\b(para|pra|as|às|à|a|no|na|em|de)\b\s*",
            "", RegexOptions.IgnoreCase);

        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ");
        return cleaned.Trim(' ', ',', '.', '-', ';');
    }

    /// <summary>Formato exigido pela URL de template do Google Agenda: 20260806T150000</summary>
    public static string ToGoogleCalendarFormat(DateTime dt) =>
        dt.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
}
