using Slender.Engine;
using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public class ExcelMacroInputTests
{
    [Theory]
    [InlineData("MyMacro")]
    [InlineData("Book1.xlsm!MyMacro")]
    [InlineData("  Module1.Tidy  ")]
    public void Valid_names_make_an_action_with_the_trimmed_name(string typed)
    {
        (ActionSpec.ExcelMacro? action, string? error) = ExcelMacroInput.Check(typed);
        Assert.Null(error);
        Assert.Equal(typed.Trim(), action!.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_typed_is_not_an_error_but_cannot_be_saved(string? typed)
    {
        (ActionSpec.ExcelMacro? action, string? error) = ExcelMacroInput.Check(typed);
        Assert.Null(action);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("My Macro")]
    [InlineData("1Macro")]
    [InlineData("Run()")]
    [InlineData("C:\\x.xlsm!Run")]
    [InlineData("Run;Other")]
    public void Invalid_names_give_a_plain_error_and_no_action(string typed)
    {
        (ActionSpec.ExcelMacro? action, string? error) = ExcelMacroInput.Check(typed);
        Assert.Null(action);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Too_long_says_so()
    {
        (_, string? error) = ExcelMacroInput.Check(new string('a', 200));
        Assert.Contains("too long", error);
    }

    [Fact]
    public void The_label_reads_Excel_macro_with_the_name()
    {
        Assert.Equal("Excel macro: Book1.xlsm!Go", ActionCatalog.Describe(new ActionSpec.ExcelMacro("Book1.xlsm!Go")).Text);
    }
}
