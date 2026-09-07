using AssetRipper.TextureDecoder.Bc;
using AssetRipper.TextureDecoder.Rgb.Formats;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using System.Runtime.InteropServices;

// Offline, read-only game-file extraction. No keys, game processes or runtime injection.
if (args.Length < 2) throw new ArgumentException("Supply the Paks directory and output directory; optional third argument is known-items.json.");
string paks = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
if (output.StartsWith(paks.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
    string.Equals(output, paks, StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Output must be outside the game archive directory.");
foreach (string toc in Directory.EnumerateFiles(paks, "*.utoc"))
{
    using var stream = File.OpenRead(toc);
    var header = new byte[81]; stream.ReadExactly(header);
    if ((header[80] & 2) != 0) throw new InvalidDataException("Encrypted IoStore is unsupported; no keys are requested or used.");
}
Directory.CreateDirectory(Path.Combine(output, "icons"));
using var provider = new DefaultFileProvider(paks, SearchOption.TopDirectoryOnly,
    new VersionContainer(EGame.GAME_UE5_6), StringComparer.OrdinalIgnoreCase);
provider.MappingsContainer = new ItemMappings();
provider.Initialize(); provider.Mount(); provider.PostMount();
string tablePath = provider.Files.Keys.Single(p => p.EndsWith("/DT_PickUpItems.uasset", StringComparison.OrdinalIgnoreCase));
var exports = JArray.Parse(JsonConvert.SerializeObject(provider.LoadPackage(tablePath).GetExports()));
var rows = (JObject)exports.Single(o => (string?)o["Type"] == "DataTable")["Rows"]!;
if (rows.Count == 0) throw new InvalidDataException("Item table did not deserialize; no metadata will be invented.");
File.WriteAllText(Path.Combine(output, "table-source.json"), exports.ToString());
Dictionary<string,string>? known = args.Length > 2
    ? JObject.Parse(File.ReadAllText(args[2]))["choices"]!.Values<string>().OfType<string>().ToDictionary(s=>s,s=>s,StringComparer.OrdinalIgnoreCase)
    : null;
var catalog = new JObject();
var evidence = new JObject();
int images = 0;
foreach (var row in rows.Properties())
{
    string raw = row.Name;
    if (known != null && !known.TryGetValue(row.Name, out raw!)) continue;
    var metadata = new JObject(); catalog[raw] = metadata;
    var data = (JObject)row.Value;
    string? label = data.Properties().FirstOrDefault(p => p.Name.StartsWith("DisplayName", StringComparison.Ordinal))?.Value.Value<string>();
    if (!string.IsNullOrWhiteSpace(label)) metadata["name"] = label;
    string? definition = data.Properties().FirstOrDefault(p => p.Name.StartsWith("ItemClass", StringComparison.Ordinal))?.Value["AssetPathName"]?.Value<string>();
    if (string.IsNullOrWhiteSpace(definition)) continue;
    string file = VirtualPath(definition.Split('.')[0]);
    try
    {
        var item = (IoPackage)provider.LoadPackage(file);
        // The definition must reference exactly one icon asset; ambiguous/absent references stay blank.
        var candidates = item.NameMap.Select(n => n.Name).OfType<string>().Where(n => n.StartsWith("/Game/", StringComparison.Ordinal) &&
            n.Contains("/Icons/", StringComparison.OrdinalIgnoreCase)).Select(VirtualPath)
            .Where(provider.Files.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        evidence[raw] = new JObject { ["tableRow"] = row.Name, ["definition"] = definition, ["candidates"] = new JArray(candidates) };
        if (candidates.Length != 1) continue;
        var texture = (IoPackage)provider.LoadPackage(candidates[0]);
        string filename = raw + ".png";
        if (raw.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
        if (!SaveIcon(texture, provider.SaveAsset(candidates[0]), Path.Combine(output, "icons", filename))) continue;
        metadata["icon"] = "icons/" + filename;
        images++;
    }
    catch (Exception ex) { evidence[raw] = new JObject { ["definition"] = definition, ["error"] = ex.Message }; }
}
File.WriteAllText(Path.Combine(output, "items.json"), catalog.ToString());
File.WriteAllText(Path.Combine(output, "extraction-evidence.json"), evidence.ToString());
VerifyIcons(catalog, output);
Console.WriteLine($"Extracted {catalog.Count} item entries and {images} directly referenced icons to {output}");

static string VirtualPath(string gamePath) => "MortalShell2/Content/" + gamePath[6..] + ".uasset";

static void VerifyIcons(JObject catalog, string output)
{
    var icons = catalog.Properties().Where(p=>p.Value["icon"]!=null).ToArray();
    if (icons.Length == 0) return;
    using var sheet = new SKBitmap(8*144, ((icons.Length+7)/8)*160);
    using var canvas = new SKCanvas(sheet);
    canvas.Clear(new SKColor(28,31,33));
    using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI"), 11);
    using var paint = new SKPaint {Color=SKColors.White,IsAntialias=true};
    for(int i=0;i<icons.Length;i++)
    {
        string path=Path.Combine(output,icons[i].Value["icon"]!.Value<string>()!);
        using var bitmap=SKBitmap.Decode(path) ?? throw new InvalidDataException("PNG decode failed: "+path);
        if(bitmap.Width<32 || bitmap.Width>128 || bitmap.Height<32 || bitmap.Height>128 || !bitmap.Pixels.Any(p=>p.Alpha!=0))
            throw new InvalidDataException("Invalid/empty icon: "+path);
        int x=i%8*144,y=i/8*160;
        canvas.DrawBitmap(bitmap,x+8,y+4);
        canvas.DrawText(icons[i].Name,x+5,y+148,font,paint);
    }
    using var image=SKImage.FromBitmap(sheet);
    using var data=image.Encode(SKEncodedImageFormat.Png,100);
    File.WriteAllBytes(Path.Combine(output,"contact-sheet.png"),data.ToArray());
    Console.WriteLine($"Verified {icons.Length} PNGs: decodable, nonempty, 32–128 px");
}

static bool SaveIcon(IoPackage package, byte[] asset, string destination)
{
    // UI BC7 textures have inline mip blocks indexed by IoStore. Validate dimensions and
    // exact block size instead of guessing the unversioned UTexture property layout.
    if (!package.NameMap.Any(n => n.Name == "PF_BC7") || package.ExportMap.Length != 1) return false;
    int header = BitConverter.ToInt32(asset, 4);
    foreach (var bulk in package.BulkDataMap)
    {
        long position = header + (long)bulk.SerialOffset, size = (long)bulk.SerialSize;
        if (position < header || size <= 0 || position + size + 12 > asset.Length) continue;
        int x = BitConverter.ToInt32(asset, (int)(position + size));
        int y = BitConverter.ToInt32(asset, (int)(position + size + 4));
        int z = BitConverter.ToInt32(asset, (int)(position + size + 8));
        if (x < 32 || x > 128 || y < 32 || y > 128 || z != 1 || ((x + 3) / 4) * ((y + 3) / 4) * 16 != size) continue;
        Bc7.Decompress<ColorRGBA<byte>, byte>(asset.AsSpan((int)position, (int)size).ToArray(), x, y, out var rgba);
        using var bitmap = new SKBitmap(x, y, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(destination, encoded.ToArray());
        return true;
    }
    return false;
}

sealed class ItemMappings : AbstractTypeMappingsProvider
{
    public override TypeMappings? MappingsForGame { get; protected set; } = new();
    public ItemMappings()
    {
        Add("DataTable", [("RowStruct", "ObjectProperty")]);
        // FSpartaItem confirmed in the shipped matching PDB: ID, ItemClass, DisplayName, Description.
        Add("SpartaItem", [("ID", "NameProperty"), ("ItemClass", "SoftClassProperty"), ("DisplayName", "StrProperty"), ("Description", "StrProperty")]);
    }
    private void Add(string name, (string Name, string Type)[] fields)
    {
        var properties = new Dictionary<int, CUE4Parse.MappingsProvider.PropertyInfo>();
        for (int i = 0; i < fields.Length; i++) properties[i] = new(i, fields[i].Name, new PropertyType(fields[i].Type), 1);
        MappingsForGame!.Types[name] = new Struct(MappingsForGame, name, null, properties, fields.Length);
    }
    public override void Load(string path, StringComparer? comparer = null) => throw new NotSupportedException();
    public override void Load(byte[] bytes, StringComparer? comparer = null) => throw new NotSupportedException();
    public override void Reload() { }
}
