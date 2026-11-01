using System.Globalization;

namespace VanguardSwitch
{
    /// <summary>Picks Portuguese or English text from the Windows display language.</summary>
    static class L
    {
        public static readonly bool Pt = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt";

        public static string T(string pt, string en) => Pt ? pt : en;
    }
}
