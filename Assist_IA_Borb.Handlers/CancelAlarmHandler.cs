using System.Diagnostics;
using System.Text.RegularExpressions;
using Assist_IA_Borb.Core;
using Assist_IA_Borb.Core.Handlers;

namespace Assist_IA_Borb.Handlers;

/// <summary>
/// Cancela um alarme já criado ("cancela o alarme do trabalho", "apaga o alarme das 7").
/// Casa o texto do comando com o rótulo (label) guardado pelo AlarmHandler; se sobrar
/// mais de um candidato ou nenhum, pede pra pessoa ser mais específica em vez de
/// adivinhar e cancelar o alarme errado.
/// </summary>
public sealed class CancelAlarmHandler : ICommandHandler
{
    public string IntentKey => "cancelaralarme";

    public async Task ExecuteAsync(string query, CancellationToken cancellationToken = default)
    {
        var alarms = await AlarmStore.GetActiveAsync(cancellationToken);

        if (alarms.Count == 0)
        {
            AssistantFeedback.Notify("Você não tem nenhum alarme agendado pra cancelar.");
            return;
        }

        // O classificador de intenção só remove a frase-gatilho ("cancela o alarme") e,
        // no máximo, UM artigo solto no início - sobra preposição em casos como "cancela
        // o alarme do trabalho" -> "do trabalho". Remove o restinho aqui pra o termo
        // bater certo com o rótulo guardado ("Trabalho").
        var term = Regex.Replace(query ?? string.Empty, @"^\s*\b(do|da|de|dos|das)\b\s*", "",
            RegexOptions.IgnoreCase).Trim();

        var candidates = string.IsNullOrEmpty(term)
            ? alarms
            : alarms.Where(a => a.Label.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();

        if (candidates.Count == 0)
        {
            AssistantFeedback.Notify($"Não encontrei nenhum alarme parecido com \"{term}\" pra cancelar.");
            return;
        }

        if (candidates.Count > 1)
        {
            var names = string.Join(", ", candidates.Select(a => $"\"{a.Label}\""));
            AssistantFeedback.Notify($"Encontrei mais de um alarme parecido: {names}. Seja mais específico.");
            return;
        }

        var target = candidates[0];
        var deleted = await DeleteTaskAsync(target.TaskName, cancellationToken);

        if (!deleted)
        {
            AssistantFeedback.Notify($"Não consegui cancelar o alarme \"{target.Label}\".");
            return;
        }

        AlarmStore.Remove(target.TaskName);
        TryDeleteScript(target.ScriptPath);

        AssistantFeedback.Notify($"Alarme \"{target.Label}\" cancelado.");
    }

    private static void TryDeleteScript(string scriptPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(scriptPath) && File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static async Task<bool> DeleteTaskAsync(string taskName, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Delete /TN \"{taskName}\" /F",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            process.Start();
            await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
