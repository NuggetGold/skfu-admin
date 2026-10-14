using System.Collections.ObjectModel;
using SkfuAdmin.Infrastructure;

namespace SkfuAdmin.ViewModels;

public sealed record FilterValue(string Value, string Title);
public sealed class TableFilter : ObservableObject
{
    private FilterValue _selected;
    private readonly Action<string, string> _changed;
    public TableFilter(string name, string title, IEnumerable<FilterValue> values, Action<string, string> changed) { Name = name; Title = title; Values = new ObservableCollection<FilterValue>(values); _selected = Values[0]; _changed = changed; }
    public string Name { get; } public string Title { get; } public ObservableCollection<FilterValue> Values { get; } public FilterValue Selected { get => _selected; set { if (Set(ref _selected, value)) _changed(Name, value.Value); } }
    public void Reset() => Selected = Values[0]; public void SetValue(string value) => Selected = Values.FirstOrDefault(x => x.Value == value) ?? Values[0];
}
