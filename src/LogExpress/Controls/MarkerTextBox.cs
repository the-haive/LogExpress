using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;

namespace LogExpress.Controls
{
    /// <summary>
    /// A TextBos that does not modify the selection when the user focuses on or un-focuses the control.
    /// </summary>
    public class MarkerTextBox: TextBox, IStyleable
    {
        Type IStyleable.StyleKey => typeof(TextBox);

        #region Overrides of TextBox

        /// <inheritdoc />
        protected override void OnGotFocus(GotFocusEventArgs e)
        {
            // On purpose we don't change anything
            //base.OnGotFocus(e);
        }

        /// <inheritdoc />
        protected override void OnLostFocus(RoutedEventArgs e)
        {
            // On purpose we don't change anything
            //base.OnLostFocus(e);
        }

        #endregion
    }
}
