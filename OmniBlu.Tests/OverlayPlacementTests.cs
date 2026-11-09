using OmniBlu.UI;

namespace OmniBlu.Tests;

public class OverlayPlacementTests
{
    [Fact]
    public void The_panel_sits_in_the_bottom_left_corner_with_a_margin()
    {
        // Full HD work area, 100% scaling, 300x200 panel, 16 unit margin.
        var (x, y) = OverlayPlacement.BottomLeft(0, 0, 1920, 1040, 1.0, 300, 200, 16);

        Assert.Equal(16, x);
        Assert.Equal(1040 - 216, y);
    }

    [Fact]
    public void The_size_and_margin_are_scaled_to_pixels()
    {
        // At 150% the panel is 450x300 pixels and the margin is 24 pixels.
        var (x, y) = OverlayPlacement.BottomLeft(0, 0, 2880, 1560, 1.5, 300, 200, 16);

        Assert.Equal(24, x);
        Assert.Equal(1560 - 324, y);
    }

    [Fact]
    public void A_work_area_that_does_not_start_at_zero_is_respected()
    {
        // A task bar on the left side moves the work area to the right.
        var (x, y) = OverlayPlacement.BottomLeft(48, 0, 1968, 1080, 1.0, 300, 200, 16);

        Assert.Equal(48 + 16, x);
        Assert.Equal(1080 - 216, y);
    }

    [Fact]
    public void A_panel_larger_than_the_area_stays_inside_it()
    {
        var (x, y) = OverlayPlacement.BottomLeft(10, 20, 200, 150, 1.0, 300, 200, 16);

        Assert.Equal(10, x);
        Assert.Equal(20, y);
    }

    [Fact]
    public void A_broken_scaling_value_is_treated_as_100_percent()
    {
        Assert.Equal(OverlayPlacement.BottomLeft(0, 0, 1920, 1040, 1.0, 300, 200, 16),
            OverlayPlacement.BottomLeft(0, 0, 1920, 1040, 0, 300, 200, 16));
    }
}
