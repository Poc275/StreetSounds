using System;
using Windows.UI.Xaml.Data;

namespace StreetSounds
{
    // Class that converts a binding from a mediaelement into a string for a text block
    public class TimespanBindingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            // This class is used to convert the timer elapsed text box AND
            // the slider thumb tip value. The prior is a TimeSpan object whereas
            // the latter is an integer which displays the elapsed seconds
            // So we need to check the casting to be able to handle both

            string formattedTime = "";

            // The value parameter is the data from the source object
            if (value is TimeSpan)
            {
                // We are converting from a TimeSpan object to a string
                TimeSpan time = (TimeSpan)value;
                formattedTime = string.Format("{0:00}:{1:00}", time.Minutes, time.Seconds);
            }
            else
            {
                // We are converting the slider thumb tip value which is an integer
                double elapsedSeconds = (double)value;
                int secs = (int)elapsedSeconds;
                TimeSpan time = new TimeSpan(0, 0, secs);
                formattedTime = string.Format("{0:00}:{1:00}", time.Minutes, time.Seconds);
            }

            return formattedTime;
        }

        // ConvertBack is not implemented for a OneWay binding
        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
