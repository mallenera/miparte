using System.Text.RegularExpressions;

namespace MiParte.Web.Tests;

/// <summary>
/// Guarda la CSP del front (<c>style-src-attr 'none'</c>): un <c>style=""</c> en un componente dejaría de aplicarse.
/// </summary>
public class SinEstilosEnLineaTests
{
    private static string RaizDelRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MiParte.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encuentra MiParte.sln.");
    }

    [Fact]
    public void LosComponentesYLaPaginaBaseNoUsanAtributosStyle()
    {
        var web = Path.Combine(RaizDelRepo(), "src", "Web");
        var sep = Path.DirectorySeparatorChar;
        var culpables = Directory.EnumerateFiles(web, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".razor") || f.EndsWith(".html"))
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\sstyle\s*=|<style[\s>]"))
            .Select(f => Path.GetRelativePath(web, f))
            .ToList();

        Assert.Empty(culpables);
    }
}
