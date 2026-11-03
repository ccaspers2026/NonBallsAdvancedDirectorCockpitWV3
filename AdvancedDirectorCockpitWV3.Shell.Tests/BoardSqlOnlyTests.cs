using System.IO;
using System.Linq;
using AdvancedDirectorCockpitWV3.Data;

namespace AdvancedDirectorCockpitWV3.Shell.Tests;

// 2026-11-03 board SQL-only guard: no board JSON files in the UI page, and the page never fetches data/*.json.
public class BoardSqlOnlyTests
{
    static string Root => Path.GetDirectoryName(CfdDbContextFactory.FindShellDir())!;

    [Fact]
    public void Ui_www_has_no_board_json_files()
    {
        var www = Path.Combine(Root, "AdvancedDirectorCockpitWV3.UI", "www");
        Assert.True(Directory.Exists(www));
        Assert.False(Directory.Exists(Path.Combine(www, "data")), "www\\data must not exist (board is SQL)");
        Assert.Empty(Directory.GetFiles(www, "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public void Page_reads_board_from_host_sql_route_only()
    {
        var js = File.ReadAllText(Path.Combine(Root, "AdvancedDirectorCockpitWV3.UI", "www", "advanced-director-cockpit.js"));
        Assert.DoesNotContain("fetch(\"data/\"", js);
        Assert.DoesNotContain(".json\"", js.Split('\n').Where(l => l.Contains("fetch(")).Aggregate("", (a, l) => a + l));
        Assert.Contains("fetch(\"adc-board/\"", js);
    }

    [Fact]
    public void Shell_source_has_no_board_file_paths()
    {
        var src = Directory.GetFiles(Path.Combine(Root, "AdvancedDirectorCockpitWV3"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText).Aggregate("", (a, s) => a + s);
        Assert.DoesNotContain("ConfiguredDataDir", src); Assert.DoesNotContain("string DataDir", src);
        Assert.DoesNotContain("Applications\\\\Data\\\\Advanced Director Cockpit WV3", src);
    }
}