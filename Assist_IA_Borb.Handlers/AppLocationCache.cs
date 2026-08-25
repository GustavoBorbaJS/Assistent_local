using System.Text.Json;

namespace Assist_IA_Borb.Handlers;

/// <summary>
/// Guarda em disco (%APPDATA%\Assist_IA_Borb\app_locations.json) o caminho resolvido
/// da última vez que um termo pedido pelo usuário ("abre o spotify") foi encontrado -
/// seja via app conhecido, registro do Windows, PATH ou atalho do Menu Iniciar. Assim,
/// da segunda vez em diante não precisa repetir a busca inteira (que pode envolver
/// varrer todo o PATH e o Menu Iniciar), só confere se o caminho ainda existe.
/// </summary>
internal static class AppLocationCache
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Assist_IA_Borb", "app_locations.json");

    public static string? TryGet(string searchTerm)
    {
        var map = Load();
        var key = Normalize(searchTerm);
        return map.TryGetValue(key, out var path) && File.Exists(path) ? path : null;
    }

    public static void Save(string searchTerm, string resolvedPath)
    {
        var key = Normalize(searchTerm);
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        var map = Load();
        map[key] = resolvedPath;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string Normalize(string searchTerm) => (searchTerm ?? string.Empty).Trim().ToLowerInvariant();

    private static Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return new Dictionary<string, string>();
            }

            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}
