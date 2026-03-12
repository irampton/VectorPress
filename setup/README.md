Run the installer from the repo root:

```bash
bash setup/install.sh
```

It will:

- install Linux GUI dependencies needed by Avalonia where supported
- install `.NET SDK 10` through the system package manager when possible
- fall back to a local install in `~/.dotnet` when needed
- install Avalonia templates
- restore the solution
