using System.Collections.ObjectModel;
using FIHModManager.Infrastructure;
using FIHModManager.Models;
using FIHModManager.Services;

namespace FIHModManager.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<ProfileItemViewModel> Profiles { get; } = [];

    public AsyncCommand SaveProfileCommand { get; private set; } = null!;
    public AsyncCommand ApplyProfileCommand { get; private set; } = null!;
    public AsyncCommand OverwriteProfileCommand { get; private set; } = null!;
    public AsyncCommand DeleteProfileCommand { get; private set; } = null!;

    private void InitProfiles()
    {
        SaveProfileCommand = new AsyncCommand(SaveProfileAsync, () => HasGame && !IsBusy);
        ApplyProfileCommand = new AsyncCommand(p => ApplyProfileAsync((ProfileItemViewModel)p!), _ => !IsBusy);
        OverwriteProfileCommand = new AsyncCommand(p => OverwriteProfileAsync((ProfileItemViewModel)p!));
        DeleteProfileCommand = new AsyncCommand(p => DeleteProfileAsync((ProfileItemViewModel)p!));
    }

    private void LoadProfiles()
    {
        Profiles.Clear();
        var keys = Mods.Select(m => m.Entry.Key).ToList();
        foreach (var p in _state.Profiles.OrderBy(p => p.Name)) Profiles.Add(new ProfileItemViewModel(p, keys));
    }

    private List<string> CurrentEnabledKeys() => Mods.Where(m => m.IsEnabled).Select(m => m.Entry.Key).ToList();

    private async Task SaveProfileAsync()
    {
        var dialog = new DialogViewModel
        {
            Title = Loc.T("dlg.saveProfile.title"),
            Message = Loc.T("dlg.saveProfile.msg", EnabledModCount),
            PrimaryText = Loc.T("common.save"),
            ShowInput = true,
            InputPlaceholder = Loc.T("dlg.saveProfile.placeholder"),
        };
        if (!await ShowDialogAsync(dialog)) return;

        var name = dialog.Input.Trim();
        var existing = _state.Profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) _state.Profiles.Remove(existing);
        _state.Profiles.Add(new ModProfile { Name = name, EnabledKeys = CurrentEnabledKeys(), SavedAt = DateTime.Now });
        JsonStore.SaveState(SelectedGame!, _state);
        LoadProfiles();
        ShowToast(Loc.T("toast.profileSaved", name), ToastKind.Success);
    }

    private async Task OverwriteProfileAsync(ProfileItemViewModel item)
    {
        var ok = await ShowDialogAsync(new DialogViewModel
        {
            Title = Loc.T("dlg.overwrite.title", item.Name),
            Message = Loc.T("dlg.overwrite.msg", EnabledModCount),
            PrimaryText = Loc.T("common.update"),
        });
        if (!ok) return;
        item.Profile.EnabledKeys = CurrentEnabledKeys();
        item.Profile.SavedAt = DateTime.Now;
        JsonStore.SaveState(SelectedGame!, _state);
        LoadProfiles();
        ShowToast(Loc.T("toast.profileUpdated", item.Name), ToastKind.Success);
    }

    private async Task DeleteProfileAsync(ProfileItemViewModel item)
    {
        var ok = await ShowDialogAsync(new DialogViewModel
        {
            Title = Loc.T("dlg.deleteProfile.title", item.Name),
            Message = Loc.T("dlg.deleteProfile.msg"),
            PrimaryText = Loc.T("common.delete"),
            IsDanger = true,
        });
        if (!ok) return;
        _state.Profiles.Remove(item.Profile);
        JsonStore.SaveState(SelectedGame!, _state);
        LoadProfiles();
    }

    private async Task ApplyProfileAsync(ProfileItemViewModel item)
    {
        if (!EnsureGameClosed()) return;
        var wanted = item.Profile.EnabledKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        await RunBusyAsync(Loc.T("busy.applyProfile", item.Name), async () =>
        {
            var game = SelectedGame!;
            var changes = Mods.Where(m => m.IsEnabled != wanted.Contains(m.Entry.Key)).Select(m => m.Entry).ToList();
            await Task.Run(() =>
            {
                foreach (var mod in changes) ModService.SetEnabled(game, mod, wanted.Contains(mod.Key));
            });
            await ReloadModsAsync();
            LoadProfiles();

            var missing = item.MissingCount;
            ShowToast(missing == 0
                ? Loc.T("toast.profileApplied", item.Name, changes.Count)
                : Loc.T("toast.profileMissing", missing), missing == 0 ? ToastKind.Success : ToastKind.Info);
        });
    }
}
