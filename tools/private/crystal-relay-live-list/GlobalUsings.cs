// WinForms (tray) and WPF both define Application/Size/Point/TextBox/Button/Clipboard.
// This is a WPF app; alias the WPF types as the unqualified winners.
global using Application = System.Windows.Application;
global using Size = System.Windows.Size;
global using Point = System.Windows.Point;
global using TextBox = System.Windows.Controls.TextBox;
global using Button = System.Windows.Controls.Button;
global using Clipboard = System.Windows.Clipboard;
