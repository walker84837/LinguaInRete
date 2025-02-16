using System.CommandLine;
using System.CommandLine.Rendering;
using HtmlAgilityPack;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LinguaInRete;

public class Program
{
    private static readonly HttpClient httpClient = new HttpClient();

    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Searches Italian definitions and synonyms");
        var wordArgument = new Argument<string>("word", "The word to search for");
        var vocabolarioOption = new Option<bool>(new[] { "--vocabolario", "-v" }, "Search Treccani Vocabolario");
        var sinonimoOption = new Option<bool>(new[] { "--sinonimo", "-s" }, "Search synonyms on sinonimi.it");
        var enciclopediaOption = new Option<bool>(new[] { "--enciclopedia", "-e" }, "Search Treccani Enciclopedia");

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
                    definition = await GetTreccaniDefinitionAsync(wordToSearch, true);
                }
                else if (isVocabolario)
                {
                    definition = await GetTreccaniDefinitionAsync(wordToSearch, false);
                }
                else if (isSinonimo)
                {
                    definition = await GetSinonimiDefinitionAsync(wordToSearch);
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

    private static async Task<string?> GetTreccaniDefinitionAsync(string word, bool isEnciclopedia, CancellationToken cancellationToken = default)
    {
        var url = isEnciclopedia
            ? $"https://www.treccani.it/enciclopedia/{word}"
            : $"https://www.treccani.it/vocabolario/{word}";

        var html = await httpClient.GetStringAsync(url, cancellationToken);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        HtmlNode? contentNode = isEnciclopedia
            ? doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'Term_termContent__UHoTq')]")
            : doc.DocumentNode.SelectNodes("//p[contains(@class, 'MuiTypography-root MuiTypography-bodyL css-d8t48w')]")?.ElementAtOrDefault(1);

        if (contentNode == null) return null;

        foreach (var strong in contentNode.Descendants("strong").ToList())
        {
            if (strong.Descendants("a").Any(a => a.GetAttributeValue("id", "") == "link2"))
                strong.Remove();
        }

        var processed = isEnciclopedia
            ? ProcessEnciclopediaContent(contentNode)
            : ProcessVocabolarioContent(contentNode);

        return SanitizeText(processed, word);
    }

    private static async Task<string?> GetSinonimiDefinitionAsync(string word, CancellationToken cancellationToken = default)
    {
        var html = await httpClient.GetStringAsync($"https://sinonimi.it/{word}", cancellationToken);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var contentNode = doc.DocumentNode.SelectSingleNode(
            "//div[contains(@class, 'bg-[#EFF2F1]') and contains(@class, 'border-2')]");

        if (contentNode == null) return null;

        var processed = ProcessHtmlContent(contentNode);
        return SanitizeText($"{Capitalize(word)}:\n{processed}", word);
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

    private static string SanitizeText(string text, string word)
    {
        word = "";
        return text;
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..].ToLower();
}
