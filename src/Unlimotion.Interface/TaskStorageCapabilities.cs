namespace Unlimotion.Interface
{
    public sealed class TaskStorageCapabilities
    {
        public const int CurrentTaskClassificationSchemaVersion = 1;

        public int TaskClassificationSchemaVersion { get; set; }

        public static TaskStorageCapabilities CreateCurrent() => new()
        {
            TaskClassificationSchemaVersion = CurrentTaskClassificationSchemaVersion
        };

        public static TaskStorageCapabilities CreateUnsupported() => new();
    }
}
