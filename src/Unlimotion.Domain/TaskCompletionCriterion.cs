using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unlimotion.Domain;

public class TaskCompletionCriterion : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString();
    private string _text = string.Empty;
    private bool _isSatisfied;

    public event PropertyChangedEventHandler? PropertyChanged;

    [JsonExtensionData]
    public IDictionary<string, JToken>? ExtensionData { get; set; }

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Text
    {
        get => _text;
        set => SetField(ref _text, value);
    }

    public bool IsSatisfied
    {
        get => _isSatisfied;
        set => SetField(ref _isSatisfied, value);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public override bool Equals(object? obj) =>
        obj is TaskCompletionCriterion other &&
        string.Equals(Id, other.Id, StringComparison.Ordinal) &&
        string.Equals(Text, other.Text, StringComparison.Ordinal) &&
        IsSatisfied == other.IsSatisfied;

    public override int GetHashCode() => HashCode.Combine(Id, Text, IsSatisfied);
}
