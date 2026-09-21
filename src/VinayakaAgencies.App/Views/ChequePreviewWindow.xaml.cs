using System;
using System.Windows;
using VinayakaAgencies.App.Presentation;
using VinayakaAgencies.Application.Banking;
using VinayakaAgencies.Application.Printing;

namespace VinayakaAgencies.App.Views;

/// <summary>
/// Interaction logic for ChequePreviewWindow.xaml.
/// </summary>
/// <remarks>
/// The preview a cheque run goes through before a leaf is consumed. It exists mainly so the owner
/// can check the layout against a real cheque before committing paper to it — which is exactly the
/// loop checkpoint C7.8 describes: print, measure what is out, edit the millimetres, save a new
/// template version, print again. No rebuild at any point.
/// </remarks>
public partial class ChequePreviewWindow : Window
{
    public ChequePreviewWindow()
    {
        InitializeComponent();
    }

    public ChequePreviewWindow(ChequePrintData cheque, PrintTemplateLayout layout)
        : this()
    {
        ArgumentNullException.ThrowIfNull(cheque);
        ArgumentNullException.ThrowIfNull(layout);

        Viewer.Document = ChequePrinter.Build(cheque, layout);

        HeadingText.Text = string.IsNullOrWhiteSpace(cheque.ChequeNumber)
            ? "Cheque preview"
            : $"Cheque {cheque.ChequeNumber}";

        StatusText.Text =
            $"{cheque.PayeeName} · {cheque.AmountInFigures} · {cheque.AmountInWords}";

        Title = HeadingText.Text;
    }
}
