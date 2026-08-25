using System.Diagnostics;
using System.Text.Json;

namespace Assist_IA_Borb.Handlers;

/// <summary>Um alarme criado, guardado pra permitir listar/cancelar depois.</summary>
public sealed class AlarmEntry
{
    public string TaskName { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string ScriptPath { get; set; } = string.Empty;
    public DateTime FirstOccurrence { get; set; }
    public string RecurrenceDescription { get; set; } = string.Empty;
}

/// <summary>
/// Guarda em disco (%APPDATA%\Assist_IA_Borb\alarms.json) os alarmes criados pelo
/// AlarmHandler, pra que "listar alarmes" e "cancelar alarme" consigam encontrá-los
/// pelo nome/rótulo em vez de precisar adivinhar pelo nome interno da tarefa do
/// Agendador do Windows (que tem um GUID no meio).
///
/// Alarmes do tipo ONCE se auto-removem do Agendador depois de disparar (o próprio
/// script .ps1 se deleta) - por isso, sempre que a store é lida, ela confere se a
/// tarefa ainda existe de verdade no Agendador e descarta silenciosamente as que já
/// dispararam e sumiram, sem precisar de nenhuma limpeza manual.
/// </summary>
internal static class AlarmStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Assist_IA_Borb", "alarms.json");

    public static void Add(AlarmEntry entry)
    {
        var entries = LoadRaw();
        entries.Add(entry);
        Persist(entries);
    }

    public static void Remove(string taskName)
    {
        var entries = LoadRaw();
        entries.RemoveAll(e => string.Equals(e.TaskName, taskName, StringComparison.OrdinalIgnoreCase));
        Persist(entries);
    }

    /// <summary>Devolve só os alarmes cuja tarefa ainda existe no Agendador do Windows,
    /// já removendo da store qualquer entrada órfã (tarefa ONCE que já disparou).</summary>
    public static async Task<List<AlarmEntry>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var entries = LoadRaw();
        var active = new List<AlarmEntry>();
        var changed = false;

        foreach (var entry in entries)
        {
            if (await TaskExistsAsync(entry.TaskName, cancellationToken))
            {
                active.Add(entry);
            }
            else
            {
                changed = true;
            }
        }

        if (changed)
        {
            Persist(active);
        }

        return active;
    }

    private static async Task<bool> TaskExistsAsync(string taskName, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Query /TN \"{taskName}\"",
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

    private static List<AlarmEntry> LoadRaw()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return [];
            }

            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<List<AlarmEntry>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void Persist(List<AlarmEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
