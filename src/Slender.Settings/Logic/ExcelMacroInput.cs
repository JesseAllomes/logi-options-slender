using Slender.Engine;
using Slender.Engine.ExcelAddin;

namespace Slender.Settings.Logic;

/// <summary>The picker's "Excel macro" box: what was typed, whether it can be saved, and the plain words when it cannot. Pure.</summary>
public static class ExcelMacroInput
{
    public const string Note = "Runs a VBA macro in the Excel window in front. Needs the Slender Excel add-in turned on (General page).";

    /// <summary>Checks the typed name (surrounding spaces ignored). Nothing typed is not an error; it just cannot be saved yet.</summary>
    public static (ActionSpec.ExcelMacro? Action, string? Error) Check(string? text)
    {
        string name = (text ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return (null, null);
        }

        return ExcelAddinProtocol.IsValidMacroName(name)
            ? (new ActionSpec.ExcelMacro(name), null)
            : (null, Error(name));
    }

    private static string Error(string name) => name.Length > ExcelAddinProtocol.MaxMacroNameChars
        ? $"That name is too long (at most {ExcelAddinProtocol.MaxMacroNameChars} characters)."
        : char.IsAsciiLetter(name[0]) || name[0] is '_' or '\''
            ? "Use only letters, digits and _ . ! ' (no spaces). Example: Book1.xlsm!MyMacro or MyMacro."
            : "A macro name starts with a letter or an underscore.";
}
