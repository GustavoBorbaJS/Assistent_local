using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Assist_IA_Borb.Core.Handlers;
using Assist_IA_Borb.Core.Intent;

namespace Assist_IA_Borb.Handlers;

/// <summary>
/// Cria um alarme real no Windows usando o Agendador de Tarefas (schtasks).
///
/// POR QUE NÃO O APP "ALARMES E RELÓGIO": ele não expõe nenhuma API pública nem
/// esquema de URI para criar um alarme com horário definido - o "ms-clock:" apenas
/// abre o aplicativo. O Agendador de Tarefas é a forma suportada de agendar algo
/// no Windows por fora, funciona sem privilégio de administrador (tarefa do próprio
/// usuário) e sobrevive a reinicializações.
///
/// A tarefa dispara um script PowerShell que toca um som e mostra uma janela de aviso.
/// Suporta tanto alarmes únicos ("alarme para 10:00") quanto recorrentes ("alarme
/// todo dia às 7", "alarme toda sexta às 8", "alarme de segunda a sexta às 6h30").
/// </summary>
public sealed class AlarmHandler : ICommandHandler
{
    public string IntentKey => "alarme";

    private static readonly string AlarmScriptDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Assist_IA_Borb", "alarms");

    /// <summary>Disparado com uma mensagem amigável sobre o resultado, pra UI exibir.</summary>
    public event Action<string>? OnResult;

    private static readonly Dictionary<string, string> WeekdayCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["domingo"] = "SUN",
        ["segunda"] = "MON", ["segunda-feira"] = "MON",
        ["terça"] = "TUE", ["terca"] = "TUE", ["terça-feira"] = "TUE",
        ["quarta"] = "WED", ["quarta-feira"] = "WED",
        ["quinta"] = "THU", ["quinta-feira"] = "THU",
        ["sexta"] = "FRI", ["sexta-feira"] = "FRI",
        ["sábado"] = "SAT", ["sabado"] = "SAT",
    };

    public async Task ExecuteAsync(string query, CancellationToken cancellationToken = default)
    {
        var (recurrence, queryWithoutRecurrence) = ExtractRecurrence(query);

        var parsed = PtBrDateTimeParser.Parse(queryWithoutRecurrence);

        if (parsed.DateTime is null || !parsed.HasExplicitTime)
        {
            OnResult?.Invoke(
                "Não entendi o horário do alarme. Tente algo como " +
                "\"alarme para 10:00\", \"me acorda às 7 da manhã\" ou \"alarme todo dia às 7\".");
            return;
        }

        var when = parsed.DateTime.Value;
        var label = string.IsNullOrWhiteSpace(parsed.RemainingText)
            ? "Alarme"
            : parsed.RemainingText;

        try
        {
            var rawName = $"Assist_IA_Borb_Alarme_{when:yyyyMMdd_HHmm}_{Guid.NewGuid():N}";
            var taskName = rawName[..Math.Min(rawName.Length, 60)];
            var isRecurring = recurrence is not null;

            var scriptPath = WriteAlarmScript(label, when, taskName, deletesSelfAfterFiring: !isRecurring);

            // O schtasks espera data no formato CURTO DA LOCALIDADE do Windows
            // (dd/MM/yyyy em pt-BR, MM/dd/yyyy em en-US), por isso formatamos com
            // a cultura atual em vez de fixar um padrão.
            var startDate = when.ToString(CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern,
                CultureInfo.CurrentCulture);
            var startTime = when.ToString("HH:mm", CultureInfo.InvariantCulture);

            var scheduleArgs = recurrence switch
            {
                null => $"/SC ONCE /SD {startDate} /ST {startTime}",
                { IsDaily: true } => $"/SC DAILY /ST {startTime}",
                RecurrenceInfo r => $"/SC WEEKLY /D {string.Join(',', r.DayCodes)} /ST {startTime}",
            };

            var arguments =
                $"/Create /TN \"{taskName}\" " +
                $"/TR \"powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \\\"{scriptPath}\\\"\" " +
                $"{scheduleArgs} /F";

            var result = await RunProcessAsync("schtasks.exe", arguments, cancellationToken);

            if (result.ExitCode == 0)
            {
                AlarmStore.Add(new AlarmEntry
                {
                    TaskName = taskName,
                    Label = label,
                    ScriptPath = scriptPath,
                    FirstOccurrence = when,
                    RecurrenceDescription = recurrence?.Description ?? string.Empty,
                });

                var when2 = recurrence is null
                    ? $"para {when:dd/MM/yyyy 'às' HH:mm}"
                    : $"{recurrence.Description} às {when:HH:mm}";

                OnResult?.Invoke($"Alarme \"{label}\" criado {when2}.");
            }
            else
            {
                OnResult?.Invoke($"Não consegui criar o alarme: {result.StandardError.Trim()}");
            }
        }
        catch (Exception ex)
        {
            OnResult?.Invoke($"Não consegui criar o alarme: {ex.Message}");
        }
    }

    private sealed record RecurrenceInfo(string[] DayCodes, string Description, bool IsDaily = false);

    private static readonly string[] WeekdaysOrder = ["MON", "TUE", "WED", "THU", "FRI"];

    /// <summary>
    /// Detecta frases de recorrência ANTES de mandar o texto pro PtBrDateTimeParser
    /// (que só entende "próxima ocorrência" de um dia, não "toda semana"), removendo
    /// a frase do texto pra sobrar só a parte de horário/título pro parser interpretar.
    /// </summary>
    private static (RecurrenceInfo? Recurrence, string Remaining) ExtractRecurrence(string query)
    {
        var text = query ?? string.Empty;

        var weekdays = Regex.Match(text, @"\bdias?\s+(úteis|uteis)\b", RegexOptions.IgnoreCase);
        if (weekdays.Success)
        {
            var rest = text.Remove(weekdays.Index, weekdays.Length);
            return (new RecurrenceInfo(WeekdaysOrder, "de segunda a sexta"), rest);
        }

        var range = Regex.Match(text, @"\bde\s+segunda\s+a\s+sexta\b", RegexOptions.IgnoreCase);
        if (range.Success)
        {
            var rest = text.Remove(range.Index, range.Length);
            return (new RecurrenceInfo(WeekdaysOrder, "de segunda a sexta"), rest);
        }

        var daily = Regex.Match(text, @"\btodo(s)?\s+(o\s+)?dias?\b|\bdiariamente\b", RegexOptions.IgnoreCase);
        if (daily.Success)
        {
            var rest = text.Remove(daily.Index, daily.Length);
            return (new RecurrenceInfo([], "todo dia", IsDaily: true), rest);
        }

        foreach (var (word, code) in WeekdayCodes.OrderByDescending(kv => kv.Key.Length))
        {
            var single = Regex.Match(text, $@"\btoda(s)?\s+{Regex.Escape(word)}\b", RegexOptions.IgnoreCase);
            if (single.Success)
            {
                var rest = text.Remove(single.Index, single.Length);
                return (new RecurrenceInfo([code], $"toda {word}"), rest);
            }
        }

        return (null, text);
    }

    /// <summary>
    /// Grava o script que a tarefa vai executar. Usar um arquivo .ps1 em vez de
    /// passar o comando inline evita a confusão de escape de aspas dentro do /TR
    /// do schtasks, que é bem propensa a erro.
    ///
    /// Alarmes de disparo único (não recorrentes) se auto-limpam: depois de tocar,
    /// o próprio script remove a tarefa do Agendador e se apaga - sem isso, o
    /// Agendador e a pasta de scripts acumulariam entradas mortas indefinidamente,
    /// já que "SC ONCE" não remove a tarefa sozinho depois de disparar.
    /// </summary>
    private static string WriteAlarmScript(string label, DateTime when, string taskName, bool deletesSelfAfterFiring)
    {
        Directory.CreateDirectory(AlarmScriptDir);

        var scriptPath = Path.Combine(AlarmScriptDir, $"alarme_{when:yyyyMMdd_HHmm}_{Guid.NewGuid():N}.ps1");
        var safeLabel = label.Replace("'", "''"); // escape de aspa simples do PowerShell

        var cleanupScript = deletesSelfAfterFiring
            ? $$"""

                schtasks.exe /Delete /TN '{{taskName}}' /F | Out-Null
                Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
                """
            : string.Empty;

        var script = $$"""
            Add-Type -AssemblyName PresentationFramework

            # Toca um padrão sonoro por alguns segundos
            1..6 | ForEach-Object {
                [console]::beep(880, 400)
                Start-Sleep -Milliseconds 200
            }

            [System.Windows.MessageBox]::Show(
                '{{safeLabel}}',
                'Alarme - {{when:HH:mm}}',
                'OK',
                'Information') | Out-Null
            {{cleanupScript}}
            """;

        File.WriteAllText(scriptPath, script, System.Text.Encoding.UTF8);
        return scriptPath;
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunProcessAsync(
        string fileName, string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        process.Start();

        var stdOut = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, stdOut, stdErr);
    }
}
