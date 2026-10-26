using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FirmasPDF.Models;

namespace FirmasPDF.Controls;

public partial class SignaturePlacementControl : UserControl
{
    public event EventHandler? Selected;
    public event EventHandler? EditStarted;
    public event EventHandler? EditCompleted;
    public SignaturePlacementControl() => InitializeComponent();
    private void Select_MouseDown(object sender, MouseButtonEventArgs e) => Selected?.Invoke(this, EventArgs.Empty);
    private void Thumb_Started(object sender, DragStartedEventArgs e)
    {
        Selected?.Invoke(this, EventArgs.Empty);
        EditStarted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
    private void Move_Delta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is SignaturePlacement placement) placement.MoveBy(e.HorizontalChange, e.VerticalChange);
        e.Handled = true;
    }
    private void Resize_Delta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is SignaturePlacement placement) placement.ResizeBy(e.HorizontalChange, e.VerticalChange);
        e.Handled = true;
    }
    private void Thumb_Completed(object sender, DragCompletedEventArgs e)
    {
        EditCompleted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}
