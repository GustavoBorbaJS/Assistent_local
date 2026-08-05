namespace Assist_IA_Borb.Handlers;

/// <summary>
/// Varre todas as pastas listadas na variável de ambiente PATH atrás de um executável
/// cujo nome contenha o termo pedido. Complementa o ShortcutFinder: PATH é onde
/// ferramentas de linha de comando costumam se registrar (git, node, claude, etc);
/// apps com interface gráfica geralmente NÃO entram no PATH, esses ficam a cargo
/// da busca por atalho do Menu Iniciar.
/// </summary>
internal static class PathExecutableFinder
{
    private static readonly string[] ExecutableExtensions = [".exe", ".cmd", ".bat"];

    public static string? FindByNameContains(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return null;
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var directories = pathVariable.Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var dir in directories)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(dir);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            var match = files.FirstOrDefault(f =>
                ExecutableExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase) &&
                Path.GetFileNameWithoutExtension(f).Contains(searchTerm, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}
