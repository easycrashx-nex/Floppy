using System.Collections.Generic;

namespace Floppy.App;

/// <summary>Beschreibung eines Cheats, so wie ihn das Spiel gemeldet hat.
/// Die App kennt keine Cheats von sich aus - alles kommt über die Verbindung.</summary>
public sealed class OptionInfo
{
    public string Id = "";
    public string Label = "";
    public string Description = "";
    public string Kind = "Toggle";
    public string Scope = "OnlyMe";
    public double Min;
    public double Max = 100;
    public double Step = 1;
    public string[] Choices = System.Array.Empty<string>();

    // Aktueller Stand aus dem Spiel
    public bool BoolValue;
    public double NumberValue;
    public string TextValue = "";
    public int ChoiceIndex;
    public bool ShareWithOthers;
    public bool Available = true;
    public bool? Active;
    public double? ResetNumber;
    public int? ResetChoice;
}

public sealed class CategoryInfo
{
    public string Name = "";
    public List<OptionInfo> Options = new();
    public override string ToString() => Name;
}
