using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PdfEditor.Models;

namespace PdfEditor.Controls;

public partial class EditObjectControl : UserControl
{
    public event EventHandler? Selected;
    public event EventHandler? EditStarted;
    public event EventHandler? EditCompleted;
    public event EventHandler? TextEdited;

    public EditObjectControl() => InitializeComponent();

    private PageEditObject? Object => DataContext as PageEditObject;

    private void Select_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Selected?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void Thumb_Started(object sender, DragStartedEventArgs e)
    {
        Selected?.Invoke(this, EventArgs.Empty);
        EditStarted?.Invoke(this, EventArgs.Empty);
    }

    private void Move_Delta(object sender, DragDeltaEventArgs e) =>
        Object?.MoveBy(e.HorizontalChange, e.VerticalChange);

    private void Resize_Delta(object sender, DragDeltaEventArgs e) =>
        Object?.ResizeBy(e.HorizontalChange, e.VerticalChange);

    private void Thumb_Completed(object sender, DragCompletedEventArgs e) =>
        EditCompleted?.Invoke(this, EventArgs.Empty);

    private void Text_LostFocus(object sender, RoutedEventArgs e) =>
        TextEdited?.Invoke(this, EventArgs.Empty);
}
