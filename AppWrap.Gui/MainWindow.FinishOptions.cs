using System.Windows;
using System.Windows.Controls;
using AppWrap.Core;

namespace AppWrap.Gui;

public partial class MainWindow
{
    private StandardInstallerOptions CurrentStandardOptions => new(offerLaunch, offerStartup, offerWebsite, developerWebsite);

    private void RenderFinishOptions(StackPanel panel)
    {
        Question.Text = "What should setup offer at the end?";
        Hint.Text = "Optional choices on the installer’s final page.\nThe installing user decides; the website starts unchecked.";
        var choices = new StackPanel();
        CheckBox Choice(string caption, string id, bool selected, Action<bool> change)
        {
            var checkbox = Identify(new CheckBox { Content = caption, IsChecked = selected, FontSize = 14, MinHeight = 28, Height = 28, Margin = new(0) }, id);
            checkbox.Checked += (_, _) => change(true); checkbox.Unchecked += (_, _) => change(false);
            choices.Children.Add(checkbox); return checkbox;
        }
        Choice("Offer to open the app (optional)", "OfferLaunch", offerLaunch, value => offerLaunch = value);
        Choice("Offer Start with Windows (optional)", "OfferStartup", offerStartup, value => offerStartup = value);
        var website = Choice("Offer the developer’s website (optional)", "OfferWebsite", offerWebsite, value => offerWebsite = value);
        var label = Label("Developer website (optional)"); label.FontSize = 12; label.Margin = new(0, 5, 0, 3); choices.Children.Add(label);
        var url = Identify(new TextBox { Text = developerWebsite, IsEnabled = offerWebsite, MaxLength = 2048, MinHeight = 36, Height = 36, FontSize = 13, Padding = new(10, 4, 10, 4), ToolTip = "An absolute http:// or https:// URL" }, "DeveloperWebsite");
        url.TextChanged += (_, _) => { developerWebsite = url.Text; Error.Text = ""; };
        website.Checked += (_, _) => url.IsEnabled = true; website.Unchecked += (_, _) => { url.IsEnabled = false; Error.Text = ""; };
        choices.Children.Add(url);
        var card = new Border { Child = choices, Padding = new(12, 6, 12, 6), CornerRadius = new(8), BorderThickness = new(1) };
        card.SetResourceReference(Border.BackgroundProperty, "InputSurface"); card.SetResourceReference(Border.BorderBrushProperty, "Border"); panel.Children.Add(card);
    }
}
