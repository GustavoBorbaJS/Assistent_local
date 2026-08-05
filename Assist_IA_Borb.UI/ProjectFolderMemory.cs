using System.IO;
using System.Text.Json;

namespace Assist_IA_Borb.UI;

/// <summary>
/// Guarda em disco (%APPDATA%\Assist_IA_Borb\projects.json) a associação entre o nome
/// do projeto (como aparece no título da janela da IDE) e a pasta local escolhida pelo
/// usuário. Assim, quando a detecção automática falha e o usuário aponta a pasta
/// manualmente uma vez, nunca mais precisa apontar de novo pro mesmo projeto.
/// </summary>
internal static class ProjectFolderMemory
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Assist_IA_Borb",
        "projects.json");

    public static string? TryGet(string projectName)
    {
        var map = Load();
        return map.TryGetValue(projectName, out var path) && Directory.Exists(path) ? path : null;
    }

    public static void Save(string projectName, string folderPath)
    {
        var map = Load();
        map[projectName] = folderPath;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException)
        {
            // Falha ao persistir não é crítica - só significa que vai perguntar de novo
            // na próxima vez. Não vale quebrar o fluxo por isso.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
