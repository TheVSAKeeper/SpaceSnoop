using System.Text.Json;

namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class ProjectCleanupViewModel
{
    private void InputChanged()
    {
        if (_restoring)
        {
            return;
        }

        _inputVersion++;
        _plan = null;
        ResultText = null;
        ClearRows();
        PersistInputs();
        StatusText = "Пути изменились. Повторите поиск.";
        FindCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanClean));
    }

    private string[] ScopeRoots()
    {
        return new[] { RootPath }.Concat(AdditionalRoots)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }

    private bool SameInputs(IReadOnlyCollection<string> roots, IReadOnlyCollection<string> outputs)
    {
        return roots.SequenceEqual(ScopeRoots(), StringComparer.Ordinal)
            && outputs.SequenceEqual(CustomOutputs, StringComparer.Ordinal);
    }

    private void RestoreInputs()
    {
        _restoring = true;
        try
        {
            var roots = ReadPaths(_settings.GetStringValue(SettingsKeys.DeveloperProjectRoots));
            RootPath = roots.FirstOrDefault() ?? string.Empty;
            foreach (var path in roots.Skip(1))
            {
                AdditionalRoots.Add(path);
            }

            foreach (var path in ReadPaths(_settings.GetStringValue(SettingsKeys.DeveloperCustomOutputs)))
            {
                CustomOutputs.Add(path);
            }
        }
        finally
        {
            _restoring = false;
        }
    }

    private void PersistInputs()
    {
        _settings.SetValue(SettingsKeys.DeveloperProjectRoots, JsonSerializer.Serialize(ScopeRoots()));
        _settings.SetValue(SettingsKeys.DeveloperCustomOutputs, JsonSerializer.Serialize(CustomOutputs));
    }

    private static string[] ReadPaths(string? json)
    {
        try
        {
            return (JsonSerializer.Deserialize<string[]>(json ?? "[]") ?? [])
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
