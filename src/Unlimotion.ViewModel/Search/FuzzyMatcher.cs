using System;

namespace Unlimotion.ViewModel.Search
{
    public static class FuzzyMatcher
    {
        public static bool IsFuzzyMatch(string source, string term, int maxDistance)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(term))
                return false;

            if (source.Contains(term, StringComparison.OrdinalIgnoreCase))
                return true;

            var words = source.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var w in words)
            {
                var distance = Levenshtein.Distance(w, term, maxDistance);
                if (distance <= maxDistance)
                    return true;
            }

            return false;
        }

        public static int GetMaxDistanceForWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return 0;

            var length = word.Length;

            if (length <= 2) return 0;
            if (length <= 4) return 1;
            if (length <= 7) return 2;
            if (length <= 10) return 3;
            return 4;
        }
    }
}
