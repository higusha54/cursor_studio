# Pointer Studio

Pointer Studio is a Windows desktop utility built with .NET 8. It creates and
previews cursor packs, converts local PNG and GIF files into Windows cursor
formats, and customizes text-caret thickness.

The Windows executable and application window use the multi-resolution
Pointer Studio icon in `CursorMaster\Assets\PointerStudio.ico`.

## Build

Install the .NET 8 SDK. From PowerShell in `CursorMaster`, restore and build the
self-contained, single-file Windows application:

```powershell
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained true
```

The executable is created at
`CursorMaster\bin\Release\net8.0-windows\win-x64\publish\CursorMaster.exe`.

## Windows installer

Install Inno Setup 6, publish the application as above, then compile
`CursorMaster\Installer\PointerStudio.iss`. The setup executable is written to
`CursorMaster\Installer\Output\PointerStudioSetup.exe`. It installs per-user
without administrator privileges, adds a Start menu shortcut, and offers an
optional desktop shortcut. English is the default installer language, and
Russian, Japanese, and Danish are also available. Its welcome and completion
pages use the two artworks supplied for those screens, fitted without
distorting their aspect ratios.

## Features

- Create cursor packs from the current Windows assignments. Each pack copies
  its `.cur` and `.ani` files into the current user's local app-data folder
  and registers a scheme that appears in `main.cpl` → **Pointers** → **Scheme**.
- Open cursor `.inf` files in Pointer Studio for a role-by-role preview, source
  cursor preview (including animated `.ani` playback), and safe pack import.
  The app reads only cursor scheme entries and nearby `.cur`/`.ani` files; it
  never executes INF commands or installs driver directives.
- Preview standalone `.ani`, `.cur`, and `.ico` files from the INF preview
  workspace; animated `.ani` cursors play in the preview panel.
- Register Pointer Studio as the current user's `.inf` file handler. Windows
  protects existing default-app selections, so if Windows already has a
  protected `.inf` handler, choose Pointer Studio once in **Default Apps**.
- Convert transparent PNG images into `.cur` files with a configurable
  hotspot; larger images are proportionally resized to fit 256 × 256 pixels,
  and the hotspot is scaled with them.
- Convert animated GIF files into `.ani` files while retaining frame delays;
  oversized frames are proportionally resized and the hotspot follows them.
- Adjust text-caret thickness and open Windows Accessibility settings for the
  text-cursor indicator.
- Use the themed **Pack studio**, **Cursor compiler**, and **Text cursor**
  workspaces. Cursor roles themselves remain managed through Windows Mouse
  Properties rather than a separate pointer-assignment screen in Pointer Studio.
- The interface uses solid dark panels and square navigation/buttons for clear,
  stable rendering without translucent or rounded effects.

Image conversion runs locally. Pointer assignments affect the current Windows
user; keep custom cursor files at their saved locations.
