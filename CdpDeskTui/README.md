# CdpDeskTui (spike)

Terminal.Gui v2 projector of the CDP desk metaphor: **P | F | M** seats, thin status chrome.

Not an IDE. No IntelliSense here — density / multi-seat feel only. Live CDP wire = later peel.

## Run

```powershell
cd CdpDeskTui
dotnet run
# one seat per monitor:
dotnet run -- --seat=p
dotnet run -- --seat=f
dotnet run -- --seat=m
```

Keys: `r` refresh fixture · `q` / `Ctrl+Q` quit · drag seat borders to resize.
