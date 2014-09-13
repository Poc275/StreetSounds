using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;


namespace StreetSounds
{
    public sealed partial class SliderControl : Slider
    {
        public SliderControl()
        {
            this.InitializeComponent();
        }

        // Image background property
        public ImageSource BackgroundImage
        {
            set
            {
                // Only set the image if we need to
                if (sliderBackground.ImageSource != value)
                {
                    sliderBackground.ImageSource = value;
                }
            }
        }

    }
}
