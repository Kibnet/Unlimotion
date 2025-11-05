namespace Unlimotion.Interface.Extensions
{
    public static class ByteExtension
    {
        public static string SizeCalculating(this long size)
        {
            var sizes = new[] { "B", "KB", "MB", "GB", "TB" };
            var order = 0;
            var result = string.Empty;

            while (size >= 1000 && order < sizes.Length - 1)
            {
                order++;

                size /= 1000;
            }

            result = $"{size} {sizes[order]}";
            return result;
        }
    }
}
