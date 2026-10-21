using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public class SliderWheelTests
{
    [Fact]
    public void One_notch_moves_one_step_up_or_down_and_stops_at_the_ends()
    {
        int carry = 0;
        Assert.Equal(55, SliderWheel.Apply(50, 0, 100, 5, 120, ref carry));
        Assert.Equal(45, SliderWheel.Apply(50, 0, 100, 5, -120, ref carry));
        Assert.Equal(100, SliderWheel.Apply(98, 0, 100, 5, 240, ref carry));
        Assert.Equal(0, SliderWheel.Apply(3, 0, 100, 5, -120, ref carry));
        Assert.Equal(0, carry);
    }

    [Fact]
    public void Parts_of_a_notch_add_up_before_the_slider_moves()
    {
        int carry = 0;
        Assert.Equal(10, SliderWheel.Apply(10, 0, 100, 1, 40, ref carry));
        Assert.Equal(10, SliderWheel.Apply(10, 0, 100, 1, 40, ref carry));
        Assert.Equal(11, SliderWheel.Apply(10, 0, 100, 1, 40, ref carry));
        Assert.Equal(0, carry);
    }

    [Fact]
    public void A_slider_with_no_step_moves_by_one()
    {
        int carry = 0;
        Assert.Equal(8, SliderWheel.Apply(7, 0, 10, 0, 120, ref carry));
    }
}
