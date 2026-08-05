namespace Assist_IA_Borb.Handlers;

/// <summary>
/// Procura atalhos (.lnk) nas pastas do Menu Iniciar (todo o sistema + usuário atual)
/// e resolve o caminho real do executável que eles apontam. Usado como último recurso
/// de localização de apps, quando não há um caminho fixo conhecido nem comando de PATH.
/// </summary>
internal static class ShortcutFinder
{
    public static string? FindByNameContains(IEnumerable<string> searchTerms)
    {
        var terms = searchTerms.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();
        if (terms.Length == 0)
        {
            return null;
        }

        var startMenuDirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        };

        foreach (var dir in startMenuDirs)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                continue;
            }

            foreach (var shortcutPath in SafeEnumerateShortcuts(dir))
            {
                var name = Path.GetFileNameWithoutExtension(shortcutPath);
                var matches = terms.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase));

                if (!matches)
                {
                    continue;
                }

                var target = ResolveShortcutTarget(shortcutPath);
                if (!string.IsNullOrWhiteSpace(target) && File.Exists(target))
                {
                    return target;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Varre recursivamente uma pasta atrás de arquivos .lnk, pasta por pasta, testando
    /// permissão em cada nível. Diferente de Directory.EnumerateFiles(..., AllDirectories)
    /// - que aborta a busca INTEIRA assim que encontra uma única subpasta sem permissão
    /// (ex: "Administrative Tools", restrita por padrão no Windows) - essa versão só pula
    /// a subpasta problemática e continua nas demais. Evita precisar rodar como
    /// administrador só pra listar atalhos do Menu Iniciar.
    /// </summary>
    private static IEnumerable<string> SafeEnumerateShortcuts(string rootDir)
    {
        var pending = new Queue<string>();
        pending.Enqueue(rootDir);

        while (pending.Count > 0)
        {
            var currentDir = pending.Dequeue();

            string[] files;
            try
            {
                files = Directory.GetFiles(currentDir, "*.lnk");
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            string[] subDirs;
            try
            {
                subDirs = Directory.GetDirectories(currentDir);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var subDir in subDirs)
            {
                pending.Enqueue(subDir);
            }
        }
    }

    /// <summary>
    /// Lê o alvo real de um atalho .lnk via COM do Windows Script Host (WScript.Shell).
    /// Late-binding com `dynamic` porque assim não precisamos adicionar uma referência
    /// COM formal no .csproj - só o pacote Microsoft.CSharp pro `dynamic` funcionar.
    /// </summary>
    private static string? ResolveShortcutTarget(string shortcutPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return null;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            string targetPath = shortcut.TargetPath;

            return string.IsNullOrWhiteSpace(targetPath) ? null : targetPath;
        }
        catch
        {
            return null;
        }
    }
}
