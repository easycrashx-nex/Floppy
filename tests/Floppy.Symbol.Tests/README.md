The test project links the actual resolver and never launches a game. Default checks need only .NET 8:

```powershell
dotnet run --project tests/Floppy.Symbol.Tests -c Release
```

For full GUID/age, section-permission, corruption, retry and physical-file cache tests, compile the tiny synthetic source with installed Visual Studio C++ build tools. Its EXE is never executed:

```powershell
$fixture = & ./tests/Floppy.Symbol.Tests/build-fixture.ps1
dotnet run --project tests/Floppy.Symbol.Tests -c Release -- --fixture-path $fixture
```

Optional installed-game verification is read-only and accepts an explicit EXE path; its matching PDB must sit beside it:

```powershell
dotnet run --project tests/Floppy.Symbol.Tests -c Release -- --fixture-path $fixture --game-path 'D:\SteamLibrary\steamapps\common\Sparta\MortalShell2\Binaries\Win64\MortalShell2-Win64-Shipping.exe'
```

No game binaries or game data are included in these fixtures.
