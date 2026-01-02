using System.CommandLine;
using System.CommandLine.Rendering;
using HtmlAgilityPack;
using System.Text;
using System.Text.RegularExpressions;

namespace LinguaInRete;

public class Program
{
    private static readonly HttpClient httpClient = new HttpClient();

    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Cerca definizioni di parole oppure i loro sinonimi");
        var wordArgument = new Argument<string>("word", "La parola da cercare");
        var vocabolarioOption = new Option<bool>(["--vocabolario", "-v", "-voc"], "Cerca nel vocabolario Treccani");
        var sinonimoOption = new Option<bool>(["--sinonimo", "-s", "-sin"], "Cerca sinonimi su sinonimi.it");
        var enciclopediaOption = new Option<bool>(["--enciclopedia", "-e", "-enc"], "Cerca nell'enciclopedia di Treccani");

        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; AcmeInc/1.0)");
        rootCommand.AddArgument(wordArgument);
        rootCommand.AddOption(vocabolarioOption);
        rootCommand.AddOption(sinonimoOption);
        rootCommand.AddOption(enciclopediaOption);

        rootCommand.SetHandler(async (word, isVocabolario, isSinonimo, isEnciclopedia) =>
        {
            var wordToSearch = word.Replace(' ', '-');
            string? definition = null;

            try
            {
                if (isEnciclopedia)
                {
                    definition = await GetTreccaniAsync(wordToSearch, true);
                }
                else if (isVocabolario)
                {
                    definition = await GetTreccaniAsync(wordToSearch, false);
                }
                else if (isSinonimo)
                {
                    definition = await GetSinonimiAsync(wordToSearch);
                }

                if (string.IsNullOrWhiteSpace(definition))
                {
                    Console.Error.WriteLine("Error: Definition not found");
                    Environment.Exit(1);
                }

                Console.WriteLine(definition);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, wordArgument, vocabolarioOption, sinonimoOption, enciclopediaOption);

        return await rootCommand.InvokeAsync(args);
    }

    private static async Task<string?> GetTreccaniAsync(string word, bool isEnciclopedia, CancellationToken cancellationToken = default)
    {
        var url = isEnciclopedia
            ? $"https://www.treccani.it/enciclopedia/{word}"
            : $"https://www.treccani.it/vocabolario/{word}";

        var html = await httpClient.GetStringAsync(url, cancellationToken);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        HtmlNode? contentNode = isEnciclopedia
            ? doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'MuiGrid-root MuiGrid-item MuiGrid-grid-xs-12 MuiGrid-grid-lg-7 Term_termContent__pwanb term-content css-1t9ge1w')]")
            : doc.DocumentNode.SelectNodes("//p[contains(@class, 'MuiGrid-root MuiGrid-item MuiGrid-grid-xs-12 MuiGrid-grid-lg-7 Term_termContent__pwanb term-content css-1t9ge1w')]")?.ElementAtOrDefault(1);

        if (contentNode == null) return null;

        foreach (var strong in contentNode.Descendants("strong").ToList())
        {
            var vocNodes = doc.DocumentNode.SelectNodes(
                "//div[contains(@class,'term-paragraph') or contains(@class,'Term_elTermParagraph') or contains(@class,'paywall')]//p[contains(@class,'MuiTypography-root') and contains(@class,'MuiTypography-bodyL')]"
            );

            if (vocNodes == null || vocNodes.Count == 0)
            {
                vocNodes = doc.DocumentNode.SelectNodes("//p[contains(@class,'MuiTypography-root') and contains(@class,'MuiTypography-bodyL')]");
            }

            if (vocNodes == null || vocNodes.Count == 0) return null;

            foreach (var node in vocNodes)
            {
                foreach (var strong in node.Descendants("strong").ToList())
                {
                    if (strong.Descendants("a").Any(a => a.GetAttributeValue("id", "") == "link2"))
                        strong.Remove();
                }
            }

            var processedParagraphs = vocNodes.Select(n => ProcessVocabolarioContent(n));
            processed = string.Join("\n\n", processedParagraphs).Trim();
        }

        if (processed == null) return null;

        return Regex.Replace(processed, @".css(.*){(.*)}", "");
    }

    private static async Task<string?> GetSinonimiAsync(string word, CancellationToken cancellationToken = default)
    {
        var html = await httpClient.GetStringAsync($"https://sinonimi.it/{word}", cancellationToken);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var contentNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'bg-[#EFF2F1]') and contains(@class, 'border-2')]");

        if (contentNode == null) return null;

        var processed = ProcessHtmlContent(contentNode);

        var capitalizedWord = Capitalize(word);
        var sinonimiList = CleanSinonimiList(processed, capitalizedWord);
        var sinonimi = string.Join(", ", sinonimiList);

        var finalString = $"{capitalizedWord}:\n{sinonimi}";

        return finalString;
    }

    private static string ProcessEnciclopediaContent(HtmlNode node)
    {
        var sb = new StringBuilder();
        foreach (var child in node.ChildNodes)
        {
            var processed = ProcessHtmlContent(child);
            if (!string.IsNullOrWhiteSpace(processed))
            {
                sb.AppendLine(processed);
                sb.AppendLine();
            }
        }
        return sb.ToString().Trim();
    }

    private static string ProcessVocabolarioContent(HtmlNode node) =>
        ProcessHtmlContent(node).Trim();

    private static string ProcessHtmlContent(HtmlNode node)
    {
        var sb = new StringBuilder();

        if (node.NodeType == HtmlNodeType.Text)
        {
            sb.Append(HtmlEntity.DeEntitize(node.InnerText));
        }
        else if (node.NodeType == HtmlNodeType.Element)
        {
            switch (node.Name.ToLower())
            {
                case "strong":
                    sb.Append(Ansi.Text.BoldOn);
                    foreach (var child in node.ChildNodes)
                        sb.Append(ProcessHtmlContent(child));
                    sb.Append(Ansi.Text.BoldOff);
                    break;
                case "em":
                    sb.Append("\x1B[3m"); // Start italic
                    foreach (var child in node.ChildNodes)
                        sb.Append(ProcessHtmlContent(child));
                    sb.Append("\x1B[23m"); // End italic
                    break;
                default:
                    foreach (var child in node.ChildNodes)
                        sb.Append(ProcessHtmlContent(child));
                    break;
            }
        }

        return sb.ToString();
    }

    private static string[] CleanSinonimiList(string input, string word)
    {
        string headWord = word;
        string output = input;

        string patternSinonimo = @"Sinonimo di " + Regex.Escape(headWord);
        output = Regex.Replace(output, patternSinonimo, "");

        string patternContrario = @"Contrario di " + Regex.Escape(headWord) + @".*?Vedi anche:";
        output = Regex.Replace(output, patternContrario, ", ");

        output = Regex.Replace(output, @"Vedi anche:", ", ");

        return output.Split(", ");
    }

    private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..].ToLower();
}
