# LinguaInRete

[English 🇺🇸/🇬🇧](README.en.md)

Questo progetto è un'applicazione da riga di comando in C# per interagire con risorse linguistiche online.

## Istruzioni per la compilazione

Per compilare ed eseguire questo progetto, è necessario il [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) o una versione successiva.

1. Clonare la repository.
2. Navigare nella directory del progetto.
3. Eseguire il seguente comando per compilare l'applicazione:

```bash
dotnet build
```

## Come si usa

Per eseguire l'applicazione, usare il comando `dotnet run` seguito dalla parola da cercare e da un'opzione che specifichi il tipo di ricerca.

```bash
dotnet run -- <parola> [opzioni]
```

### Argomenti

* `<parola>`: La parola da cercare.

### Opzioni

* `--vocabolario`, `-v`, `-voc`: Cerca la parola nel vocabolario Treccani.
* `--sinonimo`, `-s`, `-sin`: Cerca sinonimi della parola su <https://sinonimi.it>.
* `--enciclopedia`, `-e`, `-enc`: Cerca la parola nell'enciclopedia Treccani.

### Esempi
```bash
# Cerca "casa" nel vocabolario
dotnet run -- casa -v

# Cerca i sinonimi di "felice"
dotnet run -- felice -s
```

## Licenza

Questo progetto è rilasciato sotto la licenza Mozilla Public License Version 2.0. Vedere il file [LICENSE](LICENSE) per i dettagli.
