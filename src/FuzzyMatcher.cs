using System;
using System.Text;

namespace NearbyChests
{
    /// <summary>
    /// Scores how well a query matches a name. Higher is better, -1 means no match.
    /// Every space-separated query token must match (AND). Each token is tried as:
    ///   1. substring          "bron"  -> "Bronze sword"         (best)
    ///   2. word-initials      "brsw"  -> "BRonze SWord"
    ///   3. typo-tolerant word "swrod" -> "Sword"                (worst)
    /// </summary>
    public static class FuzzyMatcher
    {
        public static int Score(string query, string target)
        {
            if (string.IsNullOrEmpty(target)) return -1;
            string q = Normalize(query);
            if (q.Length == 0) return 0;
            string t = Normalize(target);

            int total = 0;
            foreach (string token in q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int s = TokenScore(token, t);
                if (s < 0) return -1;
                total += s;
            }
            return total;
        }

        /// <summary>Lowercase, split camelCase, turn punctuation into single spaces.</summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length + 8);
            char prev = ' ';
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c))
                {
                    if (char.IsUpper(c) && char.IsLower(prev)) sb.Append(' ');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else if (sb.Length > 0 && sb[sb.Length - 1] != ' ')
                {
                    sb.Append(' ');
                }
                prev = c;
            }
            return sb.ToString().Trim();
        }

        public static int TokenScore(string q, string t)
        {
            // 1. Substring.
            int idx = t.IndexOf(q, StringComparison.Ordinal);
            if (idx >= 0)
            {
                int s = 1000 - Math.Min(idx, 50) * 2;
                if (IsWordStart(t, idx)) s += 200;
                if (q.Length == t.Length) s += 300;
                return s;
            }

            // 2. Word-initials subsequence.
            if (q.Length >= 2)
            {
                int sub = InitialsScore(q, t);
                if (sub > 0) return 400 + sub;
            }

            // 3. Typo tolerance against each word (and its prefixes).
            if (q.Length >= 3)
            {
                // People rarely get the first letter wrong; requiring it removes most false positives.
                int maxDist = q.Length < 8 ? 1 : 2;
                int best = int.MaxValue;
                foreach (string word in t.Split(' '))
                {
                    if (word.Length == 0 || word[0] != q[0]) continue;
                    best = Math.Min(best, Osa(q, word));
                    for (int len = q.Length - 1; len <= q.Length + 1; len++)
                        if (len > 0 && len < word.Length)
                            best = Math.Min(best, Osa(q, word.Substring(0, len)));
                }
                if (best <= maxDist) return 200 - best * 60;
            }

            return -1;
        }

        private static bool IsWordStart(string t, int i) => i == 0 || t[i - 1] == ' ';

        /// <summary>
        /// Matches q as a subsequence of t where each matched char either starts a word
        /// or directly follows the previous matched char. Returns 0 if impossible.
        /// </summary>
        private static int InitialsScore(string q, string t)
        {
            int n = t.Length, m = q.Length;
            if (m > n) return 0;
            const int NONE = int.MinValue;
            var prev = new int[n];
            var cur = new int[n];

            for (int j = 0; j < n; j++)
                prev[j] = (q[0] == t[j] && IsWordStart(t, j)) ? 30 - Math.Min(j, 20) : NONE;

            for (int i = 1; i < m; i++)
            {
                int bestBefore = NONE; // best prev[k] for k < j-1
                for (int j = 0; j < n; j++)
                {
                    int v = NONE;
                    if (q[i] == t[j])
                    {
                        if (j > 0 && prev[j - 1] != NONE) v = prev[j - 1] + 25;               // consecutive
                        if (IsWordStart(t, j) && bestBefore != NONE) v = Math.Max(v, bestBefore + 20); // new word
                    }
                    cur[j] = v;
                    if (j > 0 && prev[j - 1] != NONE) bestBefore = Math.Max(bestBefore, prev[j - 1]);
                }
                var tmp = prev; prev = cur; cur = tmp;
            }

            int result = NONE;
            for (int j = 0; j < n; j++) result = Math.Max(result, prev[j]);
            return result == NONE ? 0 : Math.Max(1, result);
        }

        /// <summary>Optimal string alignment distance (Levenshtein + adjacent transpositions).</summary>
        private static int Osa(string a, string b)
        {
            int n = a.Length, m = b.Length;
            if (Math.Abs(n - m) > 2) return 99;
            var d = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;
            for (int i = 1; i <= n; i++)
            for (int j = 1; j <= m; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                int v = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    v = Math.Min(v, d[i - 2, j - 2] + 1);
                d[i, j] = v;
            }
            return d[n, m];
        }
    }
}
