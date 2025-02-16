using System.CommandLine;
using System.CommandLine.Rendering;
using HtmlAgilityPack;
using System.Text;
using System.Text.RegularExpressions;

var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; AcmeInc/1.0)");

var rootCommand = new RootCommand("Searches Italian definitions and synonyms");
var wordArgument = new Argument<string>("word", "The word to search for");
var vocabolarioOption = new Option<bool>(new[] { "--vocabolario", "-v" }, "Search Treccani Vocabolario");
var sinonimoOption = new Option<bool>(new[] { "--sinonimo", "-s" }, "Search synonyms on sinonimi.it");
var enciclopediaOption = new Option<bool>(new[] { "--enciclopedia", "-e" }, "Search Treccani Enciclopedia");

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

async Task<string?> GetTreccaniDefinitionAsync(string word, bool isEnciclopedia)
{
    var url = isEnciclopedia 
        ? $"https://www.treccani.it/enciclopedia/{word}" 
        : $"https://www.treccani.it/vocabolario/{word}";

    var html = await httpClient.GetStringAsync(url);
    var doc = new HtmlDocument();
    doc.LoadHtml(html);

    HtmlNode? contentNode = isEnciclopedia
        ? doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'Term_termContent__UHoTq')]")
        : doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'css-d8t48w')]");

    if (contentNode == null) return null;

    // Remove unwanted elements
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

async Task<string?> GetSinonimiDefinitionAsync(string word)
{
    var html = await httpClient.GetStringAsync($"https://sinonimi.it/{word}");
    var doc = new HtmlDocument();
    doc.LoadHtml(html);

    var contentNode = doc.DocumentNode.SelectSingleNode(
        "//div[contains(@class, 'bg-[#EFF2F1]') and contains(@class, 'border-2')]");

    if (contentNode == null) return null;

    var processed = ProcessHtmlContent(contentNode);
    return SanitizeText($"{Capitalize(word)}:\n{processed}", word);
}

string ProcessEnciclopediaContent(HtmlNode node)
{
    var sb = new StringBuilder();
    foreach (var child in node.ChildNodes)
    {
        var processed = ProcessHtmlContent(child);
        if (!string.IsNullOrWhiteSpace(processed))
        {
            sb.AppendLine(processed);
            sb.AppendLine(); // Add paragraph spacing
        }
    }
    return sb.ToString().Trim();
}

string ProcessVocabolarioContent(HtmlNode node) => 
    ProcessHtmlContent(node).Trim();

string ProcessHtmlContent(HtmlNode node)
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
                sb.Append(Ansi.Text.ItalicOn);
                foreach (var child in node.ChildNodes)
                    sb.Append(ProcessHtmlContent(child));
                sb.Append(Ansi.Text.ItalicOff);
                break;
            default:
                foreach (var child in node.ChildNodes)
                    sb.Append(ProcessHtmlContent(child));
                break;
        }
    }
    
    return sb.ToString();
}

string SanitizeText(string text, string word)
{
    var lowerWord = word.ToLower();
    var capitalizedWord = Capitalize(lowerWord);

    return text
        .Replace("Vedi anche:", $"\n\n{Ansi.Text.UnderlinedOn}Vedi anche:{Ansi.Text.UnderlinedOff}\n\n")
        .Replace($"Sinonimo di {capitalizedWord}", 
            $"\n{Ansi.Text.BoldOn}Sinonimi di {lowerWord}{Ansi.Text.BoldOff}\n\n")
        .Replace($"Contrario di {capitalizedWord}", 
            $"\n\n{Ansi.Text.BoldOn}Contrario di {lowerWord}{Ansi.Text.BoldOff}\n\n");
}

string Capitalize(string s) => 
    string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..].ToLower();