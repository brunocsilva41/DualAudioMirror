using System;
using System.Collections;
using System.Windows;

namespace DualAudioMirror.Themes
{
    public static class ThemeManager
    {
        public static void Apply(string themeName)
        {
            string name = themeName == "Light" ? "Light" : "Dark";

            Application app = Application.Current;
            if (app != null)
            {
                var dictionaries = app.Resources.MergedDictionaries;
                ResourceDictionary existing = null;
                foreach (ResourceDictionary d in dictionaries)
                {
                    if (d != null && ((IDictionary)d).Contains("Brush.Surface"))
                    {
                        existing = d;
                        break;
                    }
                }

                ResourceDictionary theme = new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Themes/" + name + ".xaml", UriKind.Absolute)
                };

                if (existing != null)
                {
                    int index = dictionaries.IndexOf(existing);
                    dictionaries.RemoveAt(index);
                    dictionaries.Insert(index, theme);
                }
                else
                {
                    dictionaries.Add(theme);
                }
            }

            AppSettings.Current.Theme = name;
            AppSettings.Save();
        }
    }
}
