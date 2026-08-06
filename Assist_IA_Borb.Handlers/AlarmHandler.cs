using System.Diagnostics;
using System.Globalization;
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
/// </summary>
public sealed class AlarmHandler : ICommandHandler
{
    public string IntentKey => "alarme";

    private static readonly string AlarmScriptDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Assist_IA_Borb", "alarms");

    /// <summary>Disparado com uma mensagem amigável sobre o resultado, pra UI exibir.</summary>
    public event Action<string>? OnResult;

    public async Task ExecuteAsync(string query, CancellationToken cancellationToken = default)
    {
        var parsed = PtBrDateTimeParser.Parse(query);

        if (parsed.DateTime is null || !parsed.HasExplicitTime)
        {
            OnResult?.Invoke(
                "Não entendi o horário do alarme. Tente algo como " +
                "\"alarme para 10:00\" ou \"me acorda às 7 da manhã\".");
            return;
        }

        var when = parsed.DateTime.Value;
        var label = string.IsNullOrWhiteSpace(parsed.RemainingText)
            ? "Alarme"
            : parsed.RemainingText;

        try
        {
            var scriptPath = WriteAlarmScript(label, when);
            var rawName = $"Assist_IA_Borb_Alarme_{when:yyyyMMdd_HHmm}_{Guid.NewGuid():N}";
            var taskName = rawName[..Math.Min(rawName.Length, 60)];

            // O schtasks espera data no formato CURTO DA LOCALIDADE do Windows
            // (dd/MM/yyyy em pt-BR, MM/dd/yyyy em en-US), por isso formatamos com
            // a cultura atual em vez de fixar um padrão.
            var startDate = when.ToString(CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern,
                CultureInfo.CurrentCulture);
            var startTime = when.ToString("HH:mm", CultureInfo.InvariantCulture);

            var arguments =
                $"/Create /TN \"{taskName}\" " +
                $"/TR \"powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \\\"{scriptPath}\\\"\" " +
                $"/SC ONCE /SD {startDate} /ST {startTime} /F";

            var result = await RunProcessAsync("schtasks.exe", arguments, cancellationToken);

            OnResult?.Invoke(result.ExitCode == 0
                ? $"Alarme criado para {when:dd/MM/yyyy 'às' HH:mm}."
                : $"Não consegui criar o alarme: {result.StandardError.Trim()}");
        }
        catch (Exception ex)
        {
            OnResult?.Invoke($"Não consegui criar o alarme: {ex.Message}");
        }
    }

    /// <summary>
    /// Grava o script que a tarefa vai executar. Usar um arquivo .ps1 em vez de
    /// passar o comando inline evita a confusão de escape de aspas dentro do /TR
    /// do schtasks, que é bem propensa a erro.
    /// </summary>
    private static string WriteAlarmScript(string label, DateTime when)
    {
        Directory.CreateDirectory(AlarmScriptDir);

        var scriptPath = Path.Combine(AlarmScriptDir, $"alarme_{when:yyyyMMdd_HHmm}_{Guid.NewGuid():N}.ps1");
        var safeLabel = label.Replace("'", "''"); // escape de aspa simples do PowerShell

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
