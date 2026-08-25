using Microsoft.Win32;

namespace Assist_IA_Borb.Handlers;

/// <summary>
/// Consulta o registro "App Paths" do Windows
/// (HKCU/HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths), o mesmo mecanismo
/// que o "Executar" (Win+R) usa pra resolver nomes de programa sem caminho completo.
/// Praticamente todo instalador de app "grande" (Chrome, Firefox, Edge, Spotify,
/// Steam, Discord, VLC, Office, etc.) registra uma entrada aqui - é mais confiável e
/// muito mais rápido que varrer o PATH inteiro ou o Menu Iniciar procurando atalho.
/// </summary>
internal static class AppPathsFinder
{
    private const string AppPathsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

    public static string? FindByNameContains(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return null;
        }

        return SearchHive(Registry.CurrentUser, searchTerm)
            ?? SearchHive(Registry.LocalMachine, searchTerm);
    }

    private static string? SearchHive(RegistryKey hive, string searchTerm)
    {
        try
        {
            using var appPaths = hive.OpenSubKey(AppPathsKey);
            if (appPaths is null)
            {
                return null;
            }

            foreach (var subKeyName in appPaths.GetSubKeyNames())
            {
                var nameWithoutExtension = Path.GetFileNameWithoutExtension(subKeyName);
                if (!nameWithoutExtension.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var subKey = appPaths.OpenSubKey(subKeyName);
                var path = subKey?.GetValue(null) as string; // valor "(Padrão)" da subchave

                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    return path;
                }
            }
        }
        catch (System.Security.SecurityException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }
}
