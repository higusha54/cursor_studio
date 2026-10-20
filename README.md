# Pointer Studio

Pointer Studio is a Windows desktop utility for creating and previewing cursor
packs, converting PNG and GIF images into Windows cursor formats, and
customizing the text caret.


## App preview
Create pack:
<img width="1600" height="900" alt="image" src="https://github.com/user-attachments/assets/c730cf45-ea6c-4c21-ad80-aa7c923da7cb" />
.INF preview:
<img width="1600" height="900" alt="image" src="https://github.com/user-attachments/assets/37030ba5-c145-46f1-846f-5cca061dd544" />
Cursor compiler:
<img width="1600" height="900" alt="image" src="https://github.com/user-attachments/assets/8803f717-fbd1-492d-824d-489d8ed37fe6" />
Text cursor:
<img width="1600" height="900" alt="image" src="https://github.com/user-attachments/assets/84fa9ba4-483b-474f-bf42-9282876ccdb2" />


## Download

Download the latest [Pointer Studio installer](https://github.com/higusha54/cursor_studio/releases/latest/download/PointerStudioSetup.exe)
and run it. The installer is English by default and also offers Russian,
Japanese, and Danish. It installs per-user without requiring administrator
privileges.

## Build from source

Install the .NET 8 SDK and Inno Setup 6. From the `CursorMaster` directory,
run:

```powershell
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained true
```

Then compile `Installer\PointerStudio.iss` with Inno Setup 6. The installer
executable is created at `Installer\Output\PointerStudioSetup.exe`.

## Repository layout

- `CursorMaster\` — application source, assets, and installer project.
- GitHub Releases — ready-to-run Windows installer.
