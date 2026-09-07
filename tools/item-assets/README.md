# Local Mortal Shell II item images

This optional .NET 10 development tool reads installed game archives. It does not launch or access game processes, edit game files, request keys, or decrypt encrypted IoStore containers. CUE4Parse and its conversion package are pinned to `1.2.2.202609`. They are extraction-time dependencies, not Floppy runtime dependencies.

```powershell
dotnet run --project tools/item-assets/ItemAssets.csproj -- 'D:\SteamLibrary\steamapps\common\Sparta\MortalShell2\Content\Paks' scratch/item-assets/export scratch/item-assets/known-items.json
```

The optional third file has `{"choices":["Gloom", ...]}` from the read-only IPC schema. It restricts rows and preserves live ID spelling. Only unique case-insensitive matches are accepted (`shellrespec` in the table is `ShellRespec` in the live schema).

Outputs: `items.json`, `icons/*.png`, `table-source.json`, `extraction-evidence.json`, and a verification contact sheet. Only `items.json` and `icons` belong in Floppy's `Assets/MortalShell2` directory. Names are omitted when the source field is empty; categories are left to the application's conservative fallback.

The verified data source is the installed Steam build **25133113**, EXE version `++Sparta-Depot+Main+CL93241+1339-CL-0`, matching PDB GUID `F38DBDB4-2417-8441-5974-859EB7BF2F34`, age 1. The parser is deliberately fixed to the **UE 5.6 package layout**. The shipped PDB confirms `FSpartaItem` declaration order: `ID` (`FName`), `ItemClass` (`TSoftClassPtr<USpartaItemDefinition>`), `DisplayName` and `Description` (both `FString`). This supplies the tiny unversioned mapping needed for `DT_PickUpItems`; it is not a general-purpose mapping for future versions.

An image is accepted only when the corresponding item-definition asset directly references exactly one icon texture. The tool exports an existing inline BC7 mip, validates its dimensions and exact block length, and decodes it with AssetRipper's decoder. It does not assign icons by fuzzy name similarity. Unsupported formats, absent references, ambiguous references, or unavailable small mips remain without an icon. Every generated PNG is decoded again, checked for nonempty pixels and dimensions of 32–128 px, and shown on the contact sheet.

Library source: [CUE4Parse](https://github.com/FabianFG/CUE4Parse). Original item artwork and metadata remain game assets. No game packages or PDBs are included in this tool.
