using Bunit;
using ZWarden.Web.Components.Ui;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// F16 (#88): the owned <see cref="MeterBar"/> maps a value onto the <c>--meter-*</c> threshold ramp
/// (style-guide.md) — nominal &lt; 60, watch 60-85, hot &gt; 85. The fill class names are full literals so the
/// Tailwind content scan emits them (ADR 0003 condition 2); it clamps out-of-range values and carries meter a11y.
/// </summary>
public class MeterBarTests
{
    [Test]
    [Arguments(0, "bg-meter-nominal")]
    [Arguments(59.9, "bg-meter-nominal")]
    [Arguments(60, "bg-meter-watch")]
    [Arguments(85, "bg-meter-watch")]
    [Arguments(85.1, "bg-meter-hot")]
    [Arguments(100, "bg-meter-hot")]
    public async Task Fill_maps_to_the_threshold_band(double value, string fillClass)
    {
        using BunitContext ctx = new();

        var cut = ctx.Render<MeterBar>(p => p.Add(c => c.Value, value));

        await Assert.That(cut.Markup).Contains(fillClass);
        await Assert.That(cut.Markup).Contains("bg-meter-track");
    }

    [Test]
    public async Task Value_over_max_is_clamped_to_one_hundred_percent()
    {
        using BunitContext ctx = new();

        var cut = ctx.Render<MeterBar>(p => p
            .Add(c => c.Value, 9_000)
            .Add(c => c.Max, 4_000));

        // 9000/4000 = 225% clamps to 100; hot band; no overflow width.
        await Assert.That(cut.Markup).Contains("bg-meter-hot");
        await Assert.That(cut.Markup).Contains("width: 100%");
        await Assert.That(cut.Markup).Contains("100%");
    }

    [Test]
    public async Task Value_over_max_computes_the_percentage_from_the_ratio()
    {
        using BunitContext ctx = new();

        // 1000/4000 = 25% => nominal band, and the label reads 25%.
        var cut = ctx.Render<MeterBar>(p => p
            .Add(c => c.Value, 1_000)
            .Add(c => c.Max, 4_000));

        await Assert.That(cut.Markup).Contains("bg-meter-nominal");
        await Assert.That(cut.Markup).Contains("25%");
    }

    [Test]
    public async Task It_renders_meter_accessibility_attributes()
    {
        using BunitContext ctx = new();

        var cut = ctx.Render<MeterBar>(p => p
            .Add(c => c.Value, 40)
            .Add(c => c.Label, "CPU"));

        string markup = cut.Markup;
        await Assert.That(markup).Contains("role=\"meter\"");
        await Assert.That(markup).Contains("aria-valuenow=\"40\"");
        await Assert.That(markup).Contains("aria-valuemin=\"0\"");
        await Assert.That(markup).Contains("aria-valuemax=\"100\"");
        await Assert.That(markup).Contains("CPU");
    }

    [Test]
    public async Task A_zero_or_negative_max_renders_an_empty_meter_without_dividing_by_zero()
    {
        using BunitContext ctx = new();

        var cut = ctx.Render<MeterBar>(p => p
            .Add(c => c.Value, 50)
            .Add(c => c.Max, 0));

        await Assert.That(cut.Markup).Contains("bg-meter-nominal");
        await Assert.That(cut.Markup).Contains("width: 0%");
    }
}
