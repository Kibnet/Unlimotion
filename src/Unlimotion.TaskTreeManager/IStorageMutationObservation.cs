namespace Unlimotion.TaskTree;

/// <summary>A hint to remeasure bytes, including uncertain and rolled-back writes; not a save receipt.</summary>
public interface IStorageMutationObservation
{
    event System.EventHandler? StorageBytesMayHaveChanged;
}
