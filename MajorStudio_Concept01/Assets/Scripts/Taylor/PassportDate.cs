using System;
using System.Globalization;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CarryOn.Taylor
{
    public static class PassportDate
    {
        public const string Hint = "Use YYYY-MM-DD (e.g. 1950-04-12).";
        private static readonly string[] Formats =
        {
            "yyyy-M-d", "M-d-yyyy", "d-M-yyyy",
            "MMM d yyyy", "MMMM d yyyy", "d MMM yyyy", "d MMMM yyyy",
            "yyyy MMM d", "yyyy MMMM d"
        };

        private static readonly Dictionary<string, string> normalizedDates = new Dictionary<string, string>(256);

        public static bool TryNormalize(string text, out string canonical)
        {
            canonical = null;
            if (string.IsNullOrWhiteSpace(text) || text.Length > 128) return false;
            if (normalizedDates.TryGetValue(text, out canonical)) return canonical != null;
            bool valid = ParseDate(text, out canonical);
            // Cache failures too: partially typed dates are repeatedly checked by multiple views.
            if (normalizedDates.Count >= 256) normalizedDates.Clear();
            normalizedDates[text] = canonical;
            return valid;
        }

        private static bool ParseDate(string text, out string canonical)
        {
            canonical = null;
            if (string.IsNullOrWhiteSpace(text) || text.Length > 128) return false;
            text = text.Trim().Replace("年", "-").Replace("月", "-").Replace("日", "")
                .Replace('/', '-').Replace('.', '-').Replace(",", "");
            text = Regex.Replace(text, @"\s+", " ");
            DateTime? match = null;
            foreach (string format in Formats)
            {
                if (!DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out var date)) continue;
                // A numeric day/month order is accepted only when it has one possible meaning.
                if (match.HasValue && match.Value != date) return false;
                match = date;
            }
            if (!match.HasValue) return false;
            canonical = match.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return true;
        }

        public static bool TryNormalizeCard(IdentityCard source, out IdentityCard result)
        {
            result = source;
            if (!TryNormalize(source.Birthday, out var birthday)) return false;
            result.Birthday = birthday;
            result.IssueDate = IdentityCard.FixedIssueDate;
            result.ExpiryDate = IdentityCard.FixedExpiryDate;
            return true;
        }

        // Interview replies may contain a full sentence. Reject conflicting or ambiguous dates.
        public static bool TryExtract(string text, out string canonical)
        {
            if (TryNormalize(text, out canonical)) return true;
            if (string.IsNullOrWhiteSpace(text) || text.Length > 512) return false;
            const string month = @"(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:tember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)";
            string pattern = @"\b(?:\d{4}[-/.]\d{1,2}[-/.]\d{1,2}|\d{1,2}[-/.]\d{1,2}[-/.]\d{4}|\d{4}年\d{1,2}月\d{1,2}日|" +
                month + @"\s+\d{1,2},?\s+\d{4}|\d{1,2}\s+" + month + @",?\s+\d{4}|\d{4}\s+" + month + @"\s+\d{1,2})\b";
            foreach (Match match in Regex.Matches(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                if (!TryNormalize(match.Value, out var date) || (canonical != null && canonical != date))
                { canonical = null; return false; }
                canonical = date;
            }
            return canonical != null;
        }
    }
}
