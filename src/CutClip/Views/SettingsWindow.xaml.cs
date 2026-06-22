using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CutClip.Models;
using CutClip.Services;

namespace CutClip.Views;

public partial class SettingsWindow : Window
{
    private readonly AppState _state = AppState.Instance;
    private uint _hotkeyVirtualKey;
    private uint _hotkeyModifiers;

    public SettingsWindow()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        FpsCombo.SelectedItem = FpsCombo.Items.Cast<object>()
            .FirstOrDefault(i => (i as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() == _state.Fps.ToString())
            ?? FpsCombo.Items[1];

        FormatCombo.SelectedIndex = _state.OutputFormat.ToLowerInvariant() switch
        {
            "webm" => 1,
            "gif" => 2,
            _ => 0
        };

        CountdownCombo.SelectedIndex = _state.CountdownSeconds switch
        {
            3 => 1,
            5 => 2,
            _ => 0
        };

        SystemAudioCheck.IsChecked = _state.RecordSystemAudio;
        MicCheck.IsChecked = _state.RecordMicrophone;
        CopyToClipboardCheck.IsChecked = _state.CopyRecordingToClipboard;
        DisableNotificationsCheck.IsChecked = _state.DisableNotifications;

        _hotkeyVirtualKey = _state.HotkeyVirtualKey;
        _hotkeyModifiers = _state.HotkeyModifiers;
        HotkeyDisplay.Text = HotkeyBinding.Format(_hotkeyModifiers, _hotkeyVirtualKey);
    }

    private void HotkeyDisplay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        HotkeyDisplay.Focus();
        e.Handled = true;
    }

    private void HotkeyDisplay_GotFocus(object sender, RoutedEventArgs e)
    {
        HotkeyDisplay.Text = "Press a key combination…";
        HotkeyDisplay.Background = new SolidColorBrush(Color.FromRgb(235, 245, 255));
    }

    private void HotkeyDisplay_LostFocus(object sender, RoutedEventArgs e)
    {
        HotkeyDisplay.Background = new SolidColorBrush(Color.FromRgb(245, 245, 245));
        HotkeyDisplay.Text = HotkeyBinding.Format(_hotkeyModifiers, _hotkeyVirtualKey);
    }

    private void HotkeyCapture_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            _hotkeyVirtualKey = _state.HotkeyVirtualKey;
            _hotkeyModifiers = _state.HotkeyModifiers;
            HotkeyDisplay.Text = HotkeyBinding.Format(_hotkeyModifiers, _hotkeyVirtualKey);
            Keyboard.ClearFocus();
            return;
        }

        if (HotkeyBinding.IsModifierKey(key))
        {
            return;
        }

        _hotkeyModifiers = HotkeyBinding.ModifiersFromKeyboard();
        _hotkeyVirtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        HotkeyDisplay.Text = HotkeyBinding.Format(_hotkeyModifiers, _hotkeyVirtualKey);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _state.Fps = int.Parse(((System.Windows.Controls.ComboBoxItem)FpsCombo.SelectedItem).Content.ToString()!);
        _state.OutputFormat = ((System.Windows.Controls.ComboBoxItem)FormatCombo.SelectedItem).Content.ToString()!.ToLowerInvariant();
        _state.CountdownSeconds = int.Parse(((System.Windows.Controls.ComboBoxItem)CountdownCombo.SelectedItem).Content.ToString()!);
        _state.RecordSystemAudio = SystemAudioCheck.IsChecked == true;
        _state.RecordMicrophone = MicCheck.IsChecked == true;
        _state.CopyRecordingToClipboard = CopyToClipboardCheck.IsChecked == true;
        _state.DisableNotifications = DisableNotificationsCheck.IsChecked == true;
        _state.HotkeyVirtualKey = _hotkeyVirtualKey;
        _state.HotkeyModifiers = _hotkeyModifiers;

        UserSettingsService.Save(_state);

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
