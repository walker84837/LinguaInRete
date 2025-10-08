# LinguaInRete

This project is a C# command-line application to interact with online linguistic resources.

## Build Instructions

To build and run this project, you will need the [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or a later version.

1. Clone the repository.
2. Navigate to the project directory.
3. Run the following command to build the application:

```bash
dotnet build
```

## Usage

To run the application, use the `dotnet run` command followed by the word you want to look up and an option specifying the search type.

```bash
dotnet run -- <word> [options]
```

### Arguments

* `<word>`: The word to search for.

### Options

* `--vocabolario`, `-v`, `-voc`: Search for the word in the Treccani vocabulary.
* `--sinonimo`, `-s`, `-sin`: Search for synonyms of the word on sinonimi.it.
* `--enciclopedia`, `-e`, `-enc`: Search for the word in the Treccani encyclopedia.

### Examples

```bash
# Search for "casa" in the vocabulary
dotnet run -- casa -v

# Search for synonyms of "felice"
dotnet run -- felice -s
```

## License

This project is licensed under the Mozilla Public License Version 2.0. See the [LICENSE](LICENSE) file for details.
