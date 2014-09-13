using Windows.UI.Xaml.Controls;

namespace StreetSounds
{
    public sealed partial class PushpinControl : UserControl
    {
        public PushpinControl()
        {
            this.InitializeComponent();
        }

        public string PushpinTitle
        {
            set
            {
                pusphinTitle.Text = value;
            }
        }

        public string PushpinSubtitle
        {
            set
            {
                pushpinSubtitle.Text = value;
            }
        }

        public void DisplayInfo()
        {
            pusphinTitle.Visibility = Windows.UI.Xaml.Visibility.Visible;
            pushpinSubtitle.Visibility = Windows.UI.Xaml.Visibility.Visible;
        }

        public void HideInfo()
        {
            pusphinTitle.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            pushpinSubtitle.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
        }
    }
}
