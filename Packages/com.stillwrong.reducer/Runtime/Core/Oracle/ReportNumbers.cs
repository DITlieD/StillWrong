using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace StillWrong.Core.Oracle
{
    public static class ReportNumbers
    {
        public static List<string> FindUnmanifested(string draft, string manifestJson, string specWords)
        {
            List<string> offending = new List<string>();
            if (draft == null || draft.Length == 0)
            {
                return offending;
            }
            List<double> allowed = ExtractNumbers(manifestJson);
            allowed.AddRange(ExtractNumbers(specWords));
            foreach (Match match in Regex.Matches(draft, "-?[0-9]+(\\.[0-9]+)?"))
            {
                double value;
                if (!double.TryParse(match.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
                {
                    continue;
                }
                bool found = false;
                for (int i = 0; i < allowed.Count; i++)
                {
                    if (Math.Abs(allowed[i] - value) < 0.000000001)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    offending.Add(match.Value);
                }
            }
            return offending;
        }

        private static List<double> ExtractNumbers(string text)
        {
            List<double> values = new List<double>();
            if (text == null || text.Length == 0)
            {
                return values;
            }
            foreach (Match match in Regex.Matches(text, "-?[0-9]+(\\.[0-9]+)?"))
            {
                double value;
                if (double.TryParse(match.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
                {
                    values.Add(value);
                }
            }
            return values;
        }
    }
}
