using System;

namespace Unlimotion.ViewModel.Search
{
    public static class Levenshtein
    {
        /// <summary>
        /// Расстояние Левенштейна с порогом maxDistance.
        /// Если расстояние > maxDistance, возвращается maxDistance + 1.
        /// </summary>
        public static int Distance(string a, string b, int maxDistance)
        {
            // полные совпадения
            if (ReferenceEquals(a, b))
                return 0;

            if (string.IsNullOrEmpty(a))
                return (b?.Length ?? 0) <= maxDistance ? b.Length : maxDistance + 1;

            if (string.IsNullOrEmpty(b))
                return a.Length <= maxDistance ? a.Length : maxDistance + 1;

            // если строки различаются по длине больше порога — нет смысла считать
            if (Math.Abs(a.Length - b.Length) > maxDistance)
                return maxDistance + 1;

            // гарантируем, что "shorter" — самая короткая строка
            string shorter = a.Length <= b.Length ? a : b;
            string longer = a.Length > b.Length ? a : b;

            int n = shorter.Length;
            int m = longer.Length;

            var previousRow = new int[m + 1];
            var currentRow = new int[m + 1];

            // первая строка матрицы (расстояние от пустой строки до longer[0..j])
            for (int j = 0; j <= m; j++)
                previousRow[j] = j;

            for (int i = 1; i <= n; i++)
            {
                currentRow[0] = i;
                int bestInRow = currentRow[0];

                char cShort = shorter[i - 1];

                for (int j = 1; j <= m; j++)
                {
                    int cost = cShort == longer[j - 1] ? 0 : 1;

                    int deletion = previousRow[j] + 1;
                    int insertion = currentRow[j - 1] + 1;
                    int substitution = previousRow[j - 1] + cost;

                    int value = deletion;
                    if (insertion < value) value = insertion;
                    if (substitution < value) value = substitution;

                    currentRow[j] = value;

                    if (value < bestInRow)
                        bestInRow = value;
                }

                // Если лучшая стоимость в строке уже > maxDistance — нет смысла продолжать
                if (bestInRow > maxDistance)
                    return maxDistance + 1;

                // swap
                var temp = previousRow;
                previousRow = currentRow;
                currentRow = temp;
            }

            return previousRow[m];
        }
    }
}

