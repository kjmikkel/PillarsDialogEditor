using Avalonia.Controls;
using Avalonia.Input;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Avalonia.Views;

public partial class ConversationNameDialog : Window
{
    public string? Result { get; private set; }

    // Parameterless ctor: a "new conversation" dialog with no prefill. Delegates so
    // zero-arg construction still wires everything up, and makes the XAML resource
    // reachable via the runtime loader (avoids AVLN3001).
    public ConversationNameDialog() : this(null) { }

    public ConversationNameDialog(string? defaultValue)
    {
        InitializeComponent();

        // Loc, not FindResource with an English fallback: AvaloniaStringProvider already
        // returns "[Key]" for a key it cannot find, so a renamed or missing key shows up
        // immediately instead of silently rendering correct English forever.
        Title = defaultValue is null
            ? Loc.Get("Dialog_NewConversation_Title")
            : Loc.Get("Dialog_ImportConversation");

        NameBox.Text = defaultValue;

        OkButton.Click     += (_, _) => Accept();
        CancelButton.Click += (_, _) => { Result = null; Close(); };
        Opened             += (_, _) => NameBox.Focus();

        NameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)  { Accept();             e.Handled = true; }
            if (e.Key == Key.Escape) { Result = null; Close(); e.Handled = true; }
        };
    }

    private void Accept()
    {
        Result = NameBox.Text;
        Close();
    }
}
