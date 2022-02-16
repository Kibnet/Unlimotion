using System.Collections;

namespace Unlimotion.ViewModel
{
    public static class ListExtensions
    {
        public static IEnumerable GetEnumerable(this IList list)
        {
            foreach (var item in list)
            {
                yield return item;
            }
        }
    }
}