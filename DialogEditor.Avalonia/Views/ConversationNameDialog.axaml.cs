using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

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

        Title = defaultValue is null
            ? Application.Current!.FindResource("Dialog_NewConversation_Title") as string ?? "New Conversation"
            : Application.Current!.FindResource("Dialog_ImportConversation")    as string ?? "Import Conversation";

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
