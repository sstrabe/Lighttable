using PhotoProcessing.Core.Darktable;
using PhotoProcessing.Core.Editing;
using M = PhotoProcessing.Core.Darktable.Modules;

namespace PhotoProcessing.Tests;

public class DarktableTests
{
    /// <summary>History darktable 5.6.1 embedded in a default export of IMG_4899.CR2 (Canon EOS 20D).</summary>
    internal static DarktableXmp Baseline() =>
        DarktableXmp.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "baseline-IMG_4899.xmp"));

    internal static readonly BaselineInfo Info = new(0.3751, 0.3795, 1500, 1000);

    [Fact]
    public void Codec_decodes_hex_and_gz_blobs()
    {
        Assert.Equal([0x12, 0xab], ParamsCodec.Decode("12ab"));
        Assert.Equal("12ab", ParamsCodec.Encode([0x12, 0xab]));

        // channelmixerrgb's params come gz-compressed in the fixture.
        var cm = Baseline().History.Single(h => h.Operation == M.ChannelMixerRgb.Op);
        Assert.Equal(M.ChannelMixerRgb.Size, cm.Params.Length);
    }

    [Theory]
    [InlineData(M.Exposure.Op, M.Exposure.Version, M.Exposure.Size)]
    [InlineData(M.Sigmoid.Op, M.Sigmoid.Version, M.Sigmoid.Size)]
    [InlineData(M.ChannelMixerRgb.Op, M.ChannelMixerRgb.Version, M.ChannelMixerRgb.Size)]
    public void Module_layouts_match_what_darktable_writes(string op, int version, int size)
    {
        var entry = Baseline().History.Single(h => h.Operation == op);
        Assert.Equal(version, entry.ModVersion);
        Assert.Equal(size, entry.Params.Length);
    }

    [Fact]
    public void Baseline_values_decode_at_the_documented_offsets()
    {
        var history = Baseline().History;
        var exposure = new ParamsBuffer(history.Single(h => h.Operation == M.Exposure.Op).Params);
        Assert.Equal(0.7f, exposure.GetFloat(M.Exposure.ExposureEv), 4);

        var sigmoid = new ParamsBuffer(history.Single(h => h.Operation == M.Sigmoid.Op).Params);
        Assert.Equal(1.5f, sigmoid.GetFloat(M.Sigmoid.Contrast), 4);
        Assert.Equal(100f, sigmoid.GetFloat(M.Sigmoid.WhiteTarget), 4);

        var cm = new ParamsBuffer(history.Single(h => h.Operation == M.ChannelMixerRgb.Op).Params);
        Assert.Equal(M.ChannelMixerRgb.IlluminantDaylight, cm.GetInt(M.ChannelMixerRgb.Illuminant));
        Assert.Equal(4166f, cm.GetFloat(M.ChannelMixerRgb.Temperature), 0);
    }

    [Fact]
    public void Sidecar_round_trips_the_history()
    {
        var baseline = Baseline();
        var reparsed = DarktableXmp.Parse(baseline.ToSidecar(baseline.History)
            .Replace("<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>", "")
            .Replace("<?xpacket end=\"w\"?>", ""));

        Assert.Equal(baseline.History.Count, reparsed.History.Count);
        for (var i = 0; i < baseline.History.Count; i++)
        {
            Assert.Equal(baseline.History[i].Operation, reparsed.History[i].Operation);
            Assert.Equal(baseline.History[i].Params, reparsed.History[i].Params);
            Assert.Equal(baseline.History[i].BlendopParams, reparsed.History[i].BlendopParams);
        }
    }

    [Fact]
    public void Extracts_xmp_from_a_jpeg_app1_segment()
    {
        var xml = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><a/></x:xmpmeta>";
        var payload = "http://ns.adobe.com/xap/1.0/\0"u8.ToArray().Concat(System.Text.Encoding.UTF8.GetBytes(xml)).ToArray();
        var length = payload.Length + 2;
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. payload, 0xFF, 0xDA, 0, 2, 0xFF, 0xD9];

        Assert.Equal(xml, DarktableXmp.ExtractXmpPacket(jpeg));
        Assert.Null(DarktableXmp.ExtractXmpPacket([0xFF, 0xD8, 0xFF, 0xD9]));
    }

    [Fact]
    public void As_shot_illuminant_is_parsed_from_the_debug_log()
    {
        const string log = """
            1.5177 [commit color calibration]  temp=4166  xy=0.3751 0.3795 - XYZ=0.9886 1.0000 0.6468 - LMS=1.0136 0.9867 0.6633  DT_ILLUMINANT_D
            1.5612 [commit color calibration]  temp=3600  xy=0.3961 0.3771 - XYZ=1.0503 1.0000 0.6013  DT_ILLUMINANT_CUSTOM
            """;
        Assert.Equal((0.3961, 0.3771), DarktableCli.ParseAsShotXy(log));
        Assert.Null(DarktableCli.ParseAsShotXy("nothing here"));
    }

    [Fact]
    public void Rotation_crop_keeps_the_aspect_ratio()
    {
        var p = M.RotatePerspective.Create(10f, 1500, 1000);
        Assert.Equal(M.RotatePerspective.Size, p.Size);
        var (cl, cr, ct, cb) = (p.GetFloat(M.RotatePerspective.Cl), p.GetFloat(M.RotatePerspective.Cr),
            p.GetFloat(M.RotatePerspective.Ct), p.GetFloat(M.RotatePerspective.Cb));

        // Crop fractions refer to the rotated bounding box; convert back to pixels.
        var theta = 10 * Math.PI / 180;
        var boundW = 1500 * Math.Cos(theta) + 1000 * Math.Sin(theta);
        var boundH = 1500 * Math.Sin(theta) + 1000 * Math.Cos(theta);
        var aspect = (cr - cl) * boundW / ((cb - ct) * boundH);
        Assert.Equal(1.5, aspect, 3);
        Assert.Equal(1 - cl, cr, 5);
    }
}
