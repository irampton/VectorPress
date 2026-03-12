# VectorPress

VectorPress is a C# + Avalonia desktop app for turning SVG artwork into a simple 2.5D extrusion workflow.

This first slice includes:

- A desktop UI built with Avalonia
- SVG file selection
- 2D SVG preview
- Paint-color detection and grouping
- Per-color extrusion height text boxes in `mm`
- A placeholder `Export` button for the next phase

## Repo Layout

- `src/VectorPress.App` - Avalonia desktop application
- `src/VectorPress.Core` - SVG parsing and shared core logic
- `VectorPress.md` - project outline and roadmap

## Verified Toolchain

This repo was set up and built in WSL on Ubuntu `24.04.1 LTS` with:

- `.NET SDK 10.0.201`
- `Avalonia 11.3.12`
- `Svg.Skia 3.4.1`
- `SkiaSharp 3.119.2`

## Setup

The primary setup path is the repo installer:

```bash
bash setup/install.sh
```

The script is idempotent and will:

- install Linux GUI dependencies for Avalonia where supported
- install `.NET SDK 10` through the system package manager when possible
- fall back to a local install in `~/.dotnet` if package-manager install is unavailable
- install Avalonia templates
- restore the solution

After the script finishes, restart your shell if it performed a first-time local `.NET` install.

## Manual WSL Setup

If you want to do it by hand instead, these are the equivalent local-install steps:

1. Download Microsoft’s installer script:

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
```

2. Install the current stable `.NET 10` SDK:

```bash
bash /tmp/dotnet-install.sh --channel 10.0 --quality GA --install-dir "$HOME/.dotnet"
```

3. Add `.NET` to your shell path:

```bash
echo 'export PATH="$HOME/.dotnet:$PATH"' >> ~/.bashrc
source ~/.bashrc
```

4. Install Avalonia templates:

```bash
dotnet new install Avalonia.Templates
```

5. Restore the repo:

```bash
dotnet restore VectorPress.slnx
```

## Linux / WSL Notes

- Avalonia apps on WSL generally need WSLg or another working GUI/display setup.
- On this machine, `DISPLAY=:0` and `WAYLAND_DISPLAY=wayland-0` were available and the app launched successfully.
- If the app fails to start because of missing native Linux dependencies, install the standard font/display packages from the official .NET and Avalonia Linux guidance for your distro.

## Build

```bash
dotnet restore VectorPress.slnx
dotnet build VectorPress.slnx
```

## Run

```bash
dotnet run --project src/VectorPress.App/VectorPress.App.csproj
```

## Current Behavior

- Click `Open SVG`
- Choose an `.svg` file
- The left pane renders the SVG
- The right sidebar lists grouped visible paint colors
- Each detected color gets:
  - a color swatch
  - a hex tooltip on hover
  - a shape count label
  - an empty extrusion height box with a persistent `mm` label
- The `Export` button is present but intentionally does nothing yet

## Next Expansion Targets

- Persist extrusion heights into a document model
- Add shape selection and inclusion toggles
- Add unit handling and SVG transform flattening
- Add geometry generation in `VectorPress.Core`
- Add export pipeline and later 3D preview
