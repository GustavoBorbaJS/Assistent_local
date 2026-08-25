using System.IO;

namespace Assist_IA_Borb.UI;

/// <summary>
/// Cria um .gitignore padrão quando um repositório é inicializado do zero pela
/// primeira vez. Sem isso, o primeiro "git add -A" de um projeto .NET recém-criado
/// costuma acabar versionando bin/, obj/ e .vs/ por engano - lixo pesado e específico
/// de máquina que não deveria ir pro repositório.
/// </summary>
internal static class GitIgnoreHelper
{
    private const string DefaultContent = """
        # Gerado automaticamente pelo Assist_IA_Borb na primeira sincronização
        bin/
        obj/
        .vs/
        *.user
        *.suo
        node_modules/
        dist/
        build/
        .env

        """;

    /// <summary>Só escreve o arquivo se ainda não existir - nunca sobrescreve um .gitignore
    /// que a pessoa já tenha customizado.</summary>
    public static void EnsureDefaultGitIgnore(string projectPath)
    {
        var path = Path.Combine(projectPath, ".gitignore");

        if (File.Exists(path))
        {
            return;
        }

        try
        {
            File.WriteAllText(path, DefaultContent);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
