using Avalonia.Controls;

namespace OmniBlu.UI;

/// <summary>
/// A small message box. <see cref="AskAsync"/> shows a question with buttons and gives the number of the button that was
/// pressed. <see cref="ShowBusy"/> shows a message with no buttons while the app works (it cannot be closed by the user).
/// </summary>
public partial class ChoiceDialog : Window
{
    private readonly TaskCompletionSource<int> result = new();
    private int closeIndex;
    private bool busy;
    private bool mustChoose;

    public ChoiceDialog()
    {
        InitializeComponent();
        Closing += (_, e) =>
        {
            // The X button and Alt+F4 do nothing while the user must choose (or while the app works). When the app itself
            // is ending, the window closes as usual.
            if ((busy || (mustChoose && !result.Task.IsCompleted)) && e.CloseReason == WindowCloseReason.WindowClosing)
            {
                e.Cancel = true;
            }
        };
        Closed += (_, _) => result.TrySetResult(closeIndex);
    }

    /// <summary>
    /// Shows the question and waits. Returns the index of the pressed button. Closing the window with the X button gives
    /// <paramref name="closeIndex"/> (the safe choice, for example "Later"). When <paramref name="mustChoose"/> is true, the
    /// X button does nothing: the user has to press one of the buttons.
    /// </summary>
    public static Task<int> AskAsync(string title, string message, IReadOnlyList<string> buttons, int closeIndex, bool mustChoose = false)
    {
        var dialog = new ChoiceDialog { closeIndex = closeIndex, mustChoose = mustChoose };
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        for (var i = 0; i < buttons.Count; i++)
        {
            var index = i;
            var button = new Button { Content = buttons[i], MinWidth = 96, Padding = new Avalonia.Thickness(14, 6) };
            button.Click += (_, _) =>
            {
                dialog.result.TrySetResult(index);
                dialog.Close();
            };
            dialog.ButtonRow.Children.Add(button);
        }

        dialog.Show();
        dialog.Activate();
        return dialog.result.Task;
    }

    /// <summary>Shows a message with no buttons. The user cannot close it; call <see cref="FinishBusy"/> when the work is done.</summary>
    public static ChoiceDialog ShowBusy(string title, string message)
    {
        var dialog = new ChoiceDialog { busy = true };
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.Show();
        dialog.Activate();
        return dialog;
    }

    /// <summary>Changes the message of a busy dialog.</summary>
    public void SetMessage(string message) => MessageText.Text = message;

    /// <summary>Closes a busy dialog.</summary>
    public void FinishBusy()
    {
        busy = false;
        Close();
    }
}
