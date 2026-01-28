# CSharply

A Visual Studio extension for organizing C# files using the CSharply dotnet tool.

More information: [https://github.com/csim/CSharply](https://github.com/csim/CSharply)

## Features

- **Organize File**: Organize the currently active C# file.

## Requirements

The CSharply dotnet tool must be installed and available in your system PATH. It is installed automatically 
when the extension activates.

To manually install run: `dotnet tool install csharply --global`  

## Usage

### Organize Current File
1. Open a C# file (.cs)
2. Open Command Palette (`ctrl+shift+P`)
3. Run "CSharply: Organize C# File"

Note: Add a key binding (`ctrl+k ctrl+s`) to use as a shortcut.

## Commands

- `CSharply: Organize File` - Organize open C# file

## License

This extension is released under the [MIT License](LICENSE).
