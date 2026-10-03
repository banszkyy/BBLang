# BBLang

[![.Net 11.0](https://img.shields.io/badge/.NET-11.0-5C2D91?style=flat-square)](#)

## About

A **bytecode interpreted, statically-typed, embedded** language for mostly scripting purposes or simulations. It can also generate Brainfuck, and optimize functions into MSIL, or compile the whole script into a [DynamicMethod](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.dynamicmethod).

[Read more in the wiki](https://github.com/banszkyy/BBLang/wiki)

- [VSCode Extension](https://github.com/banszkyy/InterpreterVSCodeExtension) 
- [Language Server](https://github.com/banszkyy/BBLang-LanguageServer)
- [Debugger Host](https://github.com/banszkyy/BBLang-DebugHost)

## Hello World

```cs
using "https://raw.githubusercontent.com/banszkyy/BBLang/master/StandardLibrary/System.Console.bbc";

printline("hello, world");
```

Without dependencies:

```cs
[External("stdout")]
void print(char message);

void printline(temp string message)
{
    for (int i = 0; message[i]; i++)
    {
        print(message[i]);
    }
    print('\r');
    print('\n');
}

printline("hello, world");
```

## Command Line Arguments

`BBLang [options...] source`

- `--help` Prints some information about the program
- `--verbose` Prints some information about the compilation process
- `--format bytecode|brainfuck|il` Specifies which generator to use.
- `--debug` Launches the debugger screen
- `--output <file>` Writes the generated code to the specified file (this option only works for brainfuck)
- `--throw-errors` Crashes the program whenever an exception thrown. This is useful for me for debugging.
- `--print-instructions` Prints the generated instructions before execution
- `--print-memory` Prints the memory after execution
- `--basepath directory` Sets the path where source files will be searched for `using` statements. [read more](https://github.com/banszkyy/BBLang/wiki/Basic-Features#imports)
- `--dont-optimize` Disables all optimization
- `--no-debug-info` Disables debug information (if you compiling into brainfuck, generating debug information will take a lot of time)
- `--stack-size size` Specifies the stack size
- `--heap-size size` Specifies the heap size

> [!NOTE]
> **Heap size in Brainfuck:**
> 
> - If you specify zero the heap will not be initialized and wherever you try to access it, it will not compile.
> - The heap size cannot be larger than 126, sorry 🤷‍♀️

- `--no-nullcheck` Disables null check generation when dereferencing a pointer

## Building

```sh
git clone https://github.com/banszkyy/BBLang
cd BBLang
dotnet publish Utility/Utility.csproj --configuration Release --output ./out/linux-x64
```

### Unity

1. Import the `/Unity/package.json` using the Unity Package Manager. [read more](https://docs.unity3d.com/6000.0/Documentation/Manual/upm-ui-local.html)
2. Create a symlink at `/Unity/Source` pointing at `/Source`. To do so, run this inside the `/Unity` directory:
    Linux:
    ```sh
    ln -s ../Source Runtime
    ```
    Windows:
    ```sh
    mklink /J "Runtime" "..\Source"
    ```
3. In Unity, navigate to `Edit > Project Settings... > Player > Other Settings > Scripting Define Symbols` and add the `UNITY` variable. [read more](https://docs.unity3d.com/2022.3/Documentation//Manual/CustomScriptingSymbols.html)
    - If you are using the [Burst compiler](https://docs.unity3d.com/Packages/com.unity.burst@latest), add `UNITY_BURST` too. You must also add a reference to `Unity.Burst` in `/Unity/BBLang.asmdef`:
    ```json
    {
      ...
      "references": [ "Unity.Burst" ],
      ...
    }
    ```
    - If you want [profiler analytics](https://docs.unity3d.com/6000.3/Documentation/Manual/profiler-introduction.html), add `UNITY_PROFILER` too.
4. Install the necessary NuGet packages with this tool: [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) or import the dll-s manually. You only need to install these:
    - [System.Collections.Immutable](https://www.nuget.org/packages/System.Collections.Immutable)

## Troubleshooting

### api-ms-win-crt-string-l1-1-0.dll Missing Error

install [this](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist?view=msvc-170)

## Project Structure

- `/Examples` Examples for using the project as a library.
- `/StandardLibrary` Pre-implemented functions and structures and some "external function" declarations.
- `/TestFiles` Test files for testing.
- `/Source` The core functionality.
- `/Utility` The command line interface.
- `/Debugger` A terminal based debugger.

## [Tests](https://github.com/banszkyy/BBLang/blob/master/Tests.md)
