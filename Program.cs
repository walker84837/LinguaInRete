using System.CommandLine;
using System.CommandLine.Rendering;
using HtmlAgilityPack;
using System.Text;
using System.Text.RegularExpressions;

namespace LinguaInRete;

public class Program
{
    private static readonly HttpClient HttpClient = new HttpClient();

    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Cerca definizioni di parole oppure i loro sinonimi");
        var wordArgument = new Argument<string>("word", "La parola da cercare");
        var vocabolarioOption = new Option<bool>(["--vocabolario", "-v", "-voc"], "Cerca nel vocabolario Treccani");
        var sinonimoOption = new Option<bool>(["--sinonimo", "-s", "-sin"], "Cerca sinonimi su sinonimi.it");
        var enciclopediaOption = new Option<bool>(["--enciclopedia", "-e", "-enc"], "Cerca nell'enciclopedia di Treccani");

        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; AcmeInc/1.0)");
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

        var html = await HttpClient.GetStringAsync(url, cancellationToken);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Look for the main container in a robust way (don't depend on the entire class string)
        HtmlNode? contentNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'term-content')]")
                               ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'Term_termContent') or contains(@class,'Term_termContent__')]");

        if (contentNode == null) return null;

        // Search for relevant paragraphs *within* the contentNode (both for "vocabolario" and "enciclopedia")
        var vocNodes = contentNode.SelectNodes(
            ".//div[contains(@class,'term-paragraph') or contains(@class,'Term_elTermParagraph') or contains(@class,'paywall')]//p[contains(@class,'MuiTypography-root') and contains(@class,'MuiTypography-bodyL')]"
        ) ?? contentNode.SelectNodes(".//p[contains(@class,'MuiTypography-root') and contains(@class,'MuiTypography-bodyL')]");

        if (vocNodes == null || vocNodes.Count == 0) return null;

        foreach (var node in vocNodes)
        {
            var strongs = node.SelectNodes(".//strong");
            if (strongs == null) continue;

            foreach (var s in strongs.ToList())
            {
                if (s.Descendants("a").Any(a => a.GetAttributeValue("id", "") == "link2"))
                    s.Remove();
            }
        }

        var processedParagraphs = vocNodes.Select(n => ProcessHtmlContent(n).Trim());
        string processed = string.Join("\n\n", processedParagraphs).Trim();

        if (string.IsNullOrWhiteSpace(processed)) return null;

        return Regex.Replace(processed, @".css(.*){(.*)}", "");
    }

    private static async Task<string?> GetSinonimiAsync(string word, CancellationToken cancellationToken = default)
    {
        var html = await HttpClient.GetStringAsync($"https://sinonimi.it/{word}", cancellationToken);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var container = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'contenuto')]") ?? doc.DocumentNode;

        var synAnchors = container.SelectNodes(
            ".//h3[contains(translate(normalize-space(.),'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'sinonimo')]/following-sibling::p[1]//a")
            ?? container.SelectNodes("//p[contains(@class,'sinonimi')]//a")
            ?? container.SelectNodes("//div[contains(@class,'bg-[#EFF2F1]')]//p//a");

        var contraAnchors = container.SelectNodes(
            ".//h3[contains(translate(normalize-space(.),'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'contrario')]/following-sibling::p[1]//a")
            ?? container.SelectNodes("//p[contains(@class,'contrari')]//a");

        var vediAnchors = container.SelectNodes(
            ".//h4[contains(translate(normalize-space(.),'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'vedi')]/following-sibling::p[1]//a")
            ?? container.SelectNodes("//p[contains(@class,'vedianche')]//a");

        static List<string> ExtractTexts(HtmlNodeCollection? nodes)
        {
            if (nodes == null) return [];
            return nodes
                .Select(n => HtmlEntity.DeEntitize(n.InnerText ?? "").Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var synList = ExtractTexts(synAnchors);
        var contraList = ExtractTexts(contraAnchors);
        var vediList = ExtractTexts(vediAnchors);

        if (!synList.Any() && !contraList.Any() && !vediList.Any())
        {
            var processed = ProcessHtmlContent(container);
            var capitalizedWord = Capitalize(word);
            var fallbackList = CleanSinonimiList(processed, capitalizedWord)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (fallbackList.Length == 0) return null;
            return $"{Capitalize(word)}:\n{string.Join(", ", fallbackList)}";
        }

        var sb = new StringBuilder();
        var header = Capitalize(word);
        sb.AppendLine(header);

        if (synList.Any())
            sb.AppendLine($"Sinonimi: {string.Join(", ", synList)}");

        if (contraList.Any())
            sb.AppendLine($"Contrari: {string.Join(", ", contraList)}");

        if (vediList.Any())
            sb.AppendLine($"Vedi anche: {string.Join(", ", vediList)}");

        return sb.ToString().Trim();
    }

    private static string ProcessHtmlContent(HtmlNode node)
    {
        var sb = new StringBuilder();

        if (node.NodeType == HtmlNodeType.Text)
        {
            sb.Append(HtmlEntity.DeEntitize(node.InnerText));
            return sb.ToString();
        }

        if (node.NodeType != HtmlNodeType.Element)
            return string.Empty;

        switch (node.Name.ToLowerInvariant())
        {
            case "strong":
                sb.Append(Ansi.Text.BoldOn);
                foreach (var child in node.ChildNodes)
                    sb.Append(ProcessHtmlContent(child));
                sb.Append(Ansi.Text.BoldOff);
                break;

            case "em":
                sb.Append("\x1B[3m"); // italic on
                foreach (var child in node.ChildNodes)
                    sb.Append(ProcessHtmlContent(child));
                sb.Append("\x1B[23m"); // italic off
                break;

            case "br":
                sb.AppendLine();
                break;

            case "p":
                foreach (var child in node.ChildNodes)
                    sb.Append(ProcessHtmlContent(child));
                sb.AppendLine();
                break;

            case "sup":
                // ignore notes, numbers, references
                break;

            case "a":
                // keep only the link text
                foreach (var child in node.ChildNodes)
                    sb.Append(ProcessHtmlContent(child));
                break;

            case "span":
                // span is just styling, so ignore it
                foreach (var child in node.ChildNodes)
                    sb.Append(ProcessHtmlContent(child));
                break;

            default:
                foreach (var child in node.ChildNodes)
                    sb.Append(ProcessHtmlContent(child));
                break;
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
