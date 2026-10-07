using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace PhotoProcessing.Core.Darktable;

/// <summary>One entry of a darktable history stack (<c>darktable:history/rdf:Seq/rdf:li</c>).</summary>
public sealed record HistoryEntry(
    string Operation,
    bool Enabled,
    int ModVersion,
    byte[] Params,
    string MultiName,
    int MultiPriority,
    int BlendopVersion,
    string BlendopParams)
{
    public bool MultiNameHandEdited { get; init; }
}

/// <summary>
/// Reads and writes darktable's XMP history. Baselines are taken from the XMP darktable embeds in
/// exported JPEGs (requires the "develop history" export metadata flag); sidecars written from
/// them are fed back to darktable-cli as its XMP_FILE argument.
/// </summary>
public sealed class DarktableXmp
{
    public static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    public static readonly XNamespace Dt = "http://darktable.sf.net/";
    private static readonly XNamespace X = "adobe:ns:meta/";
    private static readonly byte[] XmpApp1Header = "http://ns.adobe.com/xap/1.0/\0"u8.ToArray();

    private readonly XDocument _document;

    private DarktableXmp(XDocument document, List<HistoryEntry> history)
    {
        _document = document;
        History = history;
    }

    public IReadOnlyList<HistoryEntry> History { get; }

    public static DarktableXmp Parse(string xml)
    {
        var document = XDocument.Parse(xml);
        var description = Description(document);
        var items = description.Element(Dt + "history")?.Element(Rdf + "Seq")?.Elements(Rdf + "li")
            ?? throw new InvalidDataException("XMP has no darktable:history");

        var history = items.Select(li => new HistoryEntry(
                Operation: Attr(li, "operation"),
                Enabled: Attr(li, "enabled") == "1",
                ModVersion: int.Parse(Attr(li, "modversion"), CultureInfo.InvariantCulture),
                Params: ParamsCodec.Decode(Attr(li, "params")),
                MultiName: (string?)li.Attribute(Dt + "multi_name") ?? "",
                MultiPriority: int.Parse((string?)li.Attribute(Dt + "multi_priority") ?? "0", CultureInfo.InvariantCulture),
                BlendopVersion: int.Parse(Attr(li, "blendop_version"), CultureInfo.InvariantCulture),
                BlendopParams: Attr(li, "blendop_params"))
            {
                MultiNameHandEdited = (string?)li.Attribute(Dt + "multi_name_hand_edited") == "1",
            })
            .ToList();

        // history_end can be lower than the entry count (undone steps); only the active part matters.
        var end = (int?)description.Attribute(Dt + "history_end") ?? history.Count;
        return new DarktableXmp(document, history.Take(end).ToList());
    }

    public static DarktableXmp FromJpeg(string jpegPath)
    {
        var xml = ExtractXmpPacket(File.ReadAllBytes(jpegPath))
            ?? throw new InvalidDataException(
                $"{jpegPath} has no XMP packet; is plugins/lighttable/export/metadata_flags missing the history bit (0x20)?");
        return Parse(xml);
    }

    public static DarktableXmp Load(string xmpPath) => Parse(File.ReadAllText(xmpPath));

    /// <summary>Serializes a sidecar with the given history, keeping the baseline's top-level attributes.</summary>
    public string ToSidecar(IReadOnlyList<HistoryEntry> history)
    {
        var document = new XDocument(_document);
        var description = Description(document);
        description.SetAttributeValue(Dt + "history_end", history.Count.ToString(CultureInfo.InvariantCulture));
        description.SetAttributeValue(Dt + "auto_presets_applied", "1");

        var seq = description.Element(Dt + "history")!.Element(Rdf + "Seq")!;
        seq.RemoveNodes();
        for (var i = 0; i < history.Count; i++)
        {
            var entry = history[i];
            seq.Add(new XElement(Rdf + "li",
                new XAttribute(Dt + "num", i),
                new XAttribute(Dt + "operation", entry.Operation),
                new XAttribute(Dt + "enabled", entry.Enabled ? "1" : "0"),
                new XAttribute(Dt + "modversion", entry.ModVersion),
                new XAttribute(Dt + "params", ParamsCodec.Encode(entry.Params)),
                new XAttribute(Dt + "multi_name", entry.MultiName),
                new XAttribute(Dt + "multi_name_hand_edited", entry.MultiNameHandEdited ? "1" : "0"),
                new XAttribute(Dt + "multi_priority", entry.MultiPriority),
                new XAttribute(Dt + "blendop_version", entry.BlendopVersion),
                new XAttribute(Dt + "blendop_params", entry.BlendopParams)));
        }

        var root = document.Root!;
        var sb = new StringBuilder();
        sb.Append("<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n");
        sb.Append(root.ToString());
        sb.Append("\n<?xpacket end=\"w\"?>\n");
        return sb.ToString();
    }

    public string ToXml() => _document.Root!.ToString();

    private static XElement Description(XDocument document) =>
        document.Root?.Element(Rdf + "RDF")?.Element(Rdf + "Description")
        ?? document.Root?.Element(X + "xmpmeta")?.Element(Rdf + "RDF")?.Element(Rdf + "Description")
        ?? throw new InvalidDataException("XMP has no rdf:Description");

    private static string Attr(XElement element, string name) =>
        (string?)element.Attribute(Dt + name)
        ?? throw new InvalidDataException($"history entry is missing darktable:{name}");

    /// <summary>Finds the standard XMP APP1 segment in a JPEG and returns its XML.</summary>
    public static string? ExtractXmpPacket(byte[] jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
            return null;

        var pos = 2;
        while (pos + 4 <= jpeg.Length && jpeg[pos] == 0xFF)
        {
            var marker = jpeg[pos + 1];
            if (marker == 0xDA || marker == 0xD9) // start of scan / end of image
                break;

            var length = (jpeg[pos + 2] << 8) | jpeg[pos + 3];
            var payload = jpeg.AsSpan(pos + 4, Math.Min(length - 2, jpeg.Length - pos - 4));
            if (marker == 0xE1 && payload.StartsWith(XmpApp1Header))
            {
                var xml = Encoding.UTF8.GetString(payload[XmpApp1Header.Length..]);
                var start = xml.IndexOf("<x:xmpmeta", StringComparison.Ordinal);
                var end = xml.LastIndexOf("</x:xmpmeta>", StringComparison.Ordinal);
                if (start >= 0 && end > start)
                    return xml[start..(end + "</x:xmpmeta>".Length)];
            }

            pos += 2 + length;
        }

        return null;
    }
}
