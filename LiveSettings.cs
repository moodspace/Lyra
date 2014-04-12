using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Text;
using System.Windows.Media;
namespace Lyra
{
    class LiveSettings
    {
        //this class reads settings from My.Settings at startup 
        //and serve the major way to provide real-time R/W of
        //settings, it writes settings to My.Settings at exit.

        //main form
        static internal bool autoAdd = true;
        static internal Color bgColor = (Color)ColorConverter.ConvertFromString("#FF18191B");
        static internal Color secondColor = (Color)ColorConverter.ConvertFromString("#FF34353A");
        static internal Color foreColor = Color.FromRgb(255, 255, 255); 

        //paths
        static internal String baseDir = System.AppDomain.CurrentDomain.BaseDirectory;
        static internal String settingsFN = "settings.ini";
        static internal String artistDatabaseFN = "artists.db";
        static internal String nircmdFN = "nircmdc.exe";
        static internal String jsonFN = "Newtonsoft.Json.dll";
        static internal String agilityPackFN = "HtmlAgilityPack.dll";
        static internal String elysiumFN = "Elysium.dll";

        //blacklist
        static internal List<String> blacklist = new List<String>();
        //whitelist
        static internal List<String> whitelist = new List<String>();

        internal static void ReadSettings()
        {
            try
            {
                using (StreamReader sReader = new StreamReader(settingsFN))
                {
                    while (!sReader.EndOfStream)
                    {
                        String settingsLine = sReader.ReadLine();

                        if (settingsLine.StartsWith("autoblock"))
                            autoAdd = StringToBool(settingsLine);
                        else if (settingsLine.StartsWith("bgcolor"))
                            bgColor = StringToColor(settingsLine);
                        else if (settingsLine.StartsWith("secondcolor"))
                            secondColor = StringToColor(settingsLine);
                        else if (settingsLine.StartsWith("forecolor"))
                            foreColor = StringToColor(settingsLine);
                        else
                        {
                            if (settingsLine.ToLower().StartsWith("bl: "))
                                blacklist.Add(settingsLine.Substring(4).Trim());
                            else if (settingsLine.ToLower().StartsWith("wl: "))
                                whitelist.Add(settingsLine.Substring(4).Trim());
                        }
                    }

                    sReader.Close();
                }
            }
            catch { }
        }

        internal static void WriteSettings()
        {
            //will overwrite
            StreamWriter sWriter = new StreamWriter(settingsFN, false, new UnicodeEncoding());
            sWriter.WriteLine(outputBoolSetting("autoblock", autoAdd));
            sWriter.WriteLine(outputStringSetting("bgcolor", bgColor.ToString()));
            sWriter.WriteLine(outputStringSetting("secondcolor", secondColor.ToString()));
            sWriter.WriteLine(outputStringSetting("forecolor", foreColor.ToString()));

            foreach (String entry in blacklist)
                sWriter.WriteLine("BL: " + entry);
            foreach (String entry in whitelist)
                sWriter.WriteLine("WL: " + entry);

            sWriter.Close();
        }

        //Precondition: true and false are presented in lowercase
        private static bool StringToBool(String str)
        {
            return str.EndsWith("True");
        }

        private static Color StringToColor(String str)
        {
            str = str.Trim();
            return (Color)ColorConverter.ConvertFromString(str.Substring(str.IndexOf("#")));
        }

        private static String outputBoolSetting(String settingName, bool settingVal)
        {
            return settingName + " := " + settingVal.ToString();
        }

        private static String outputStringSetting(String settingName, String settingVal)
        {
            return settingName + " := " + settingVal;
        }

    }
}